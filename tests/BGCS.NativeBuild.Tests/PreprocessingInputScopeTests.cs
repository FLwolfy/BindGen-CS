using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.Infrastructure.IO;
using BGCS.NativeBuild.Planning;
using BGCS.Platforms.Targeting;
using BGCS.Platforms.Toolchains;
using Xunit;

namespace BGCS.NativeBuild.Tests;

public sealed class PreprocessingInputScopeTests
{
    [Theory]
    [InlineData(SourceLanguage.C)]
    [InlineData(SourceLanguage.Cpp)]
    public async Task RealSourceViewIgnoresOnlyImplicitLinkMembershipWhileExplicitInputsAndFullBuildKeepTheirFences(SourceLanguage language)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs preprocessing input scope " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string headers = Directory.CreateDirectory(Path.Combine(root, "headers")).FullName;
            string implicitLibraries = Directory.CreateDirectory(Path.Combine(root, "implicit-libraries")).FullName;
            string explicitLibraries = Directory.CreateDirectory(Path.Combine(root, "explicit-libraries")).FullName;
            string requestedLibraries = Directory.CreateDirectory(Path.Combine(root, "full-build-libraries")).FullName;
            string header = Path.Combine(headers, "original.h"), response = Path.Combine(root, "driver.rsp");
            const string headerText = "#define SOURCE_VALUE 41\nint source_view_value(void);\n";
            string responseText = "-DRESPONSE_VALUE=13\n-L\n\"" + explicitLibraries.Replace('\\', '/') + "\"\n";
            await File.WriteAllTextAsync(header, headerText);
            await File.WriteAllTextAsync(response, responseText);
            string compiler = InstalledClang();
            NativeTargetDescriptor target = Target(compiler, implicitLibraries);
            FrontendRequest source = new(target, language, [header])
            { pathBase = root, arguments = ["-nostdinc", "@" + response], parseMacros = false };
            NativeSourceViewResult first = await new NativeSourceViewPreprocessor().PrepareAsync(new(source, Path.Combine(root, "first")));
            Assert.False(File.Exists(Path.Combine(root, "first", "unused.o")));
            Assert.Contains("int source_view_value(void)", first.expandedSource);
            Assert.Equal("41", Assert.Single(first.macros, macro => macro.name == "SOURCE_VALUE").replacement);
            Assert.Equal("13", Assert.Single(first.macros, macro => macro.name == "RESPONSE_VALUE").replacement);
            Assert.Contains(first.inputs.files, file => file.path == compiler);
            Assert.Contains(first.inputs.files, file => file.path == header);
            Assert.Contains(first.inputs.files, file => file.path == response);
            Assert.DoesNotContain(first.inputs.searchDirectories, directory => directory.path == implicitLibraries);
            Assert.Contains(first.inputs.searchDirectories, directory => directory.path == explicitLibraries);
            Assert.Null(SourceSnapshotVerifier.GetMismatch(first.inputs));

            // Only the linker's implicit search set changes. A second actual
            // compiler run must retain the same observed tokens and macros.
            await File.WriteAllTextAsync(Path.Combine(implicitLibraries, "new-library.lib"), "link-only membership");
            Assert.Null(SourceSnapshotVerifier.GetMismatch(first.inputs));
            NativeSourceViewResult replay = await new NativeSourceViewPreprocessor().PrepareAsync(new(source, Path.Combine(root, "replay")));
            Assert.False(File.Exists(Path.Combine(root, "replay", "unused.o")));
            Assert.Equal(first.observationFingerprint, replay.observationFingerprint);

            DateTime headerStamp = File.GetLastWriteTimeUtc(header);
            await File.WriteAllTextAsync(header, headerText.Replace("41", "42", StringComparison.Ordinal));
            File.SetLastWriteTimeUtc(header, headerStamp);
            Assert.Equal("Input file bytes changed: " + header, SourceSnapshotVerifier.GetMismatch(first.inputs));
            await File.WriteAllTextAsync(header, headerText);
            File.SetLastWriteTimeUtc(header, headerStamp);
            Assert.Null(SourceSnapshotVerifier.GetMismatch(first.inputs));

            DateTime responseStamp = File.GetLastWriteTimeUtc(response);
            await File.WriteAllTextAsync(response, responseText.Replace("=13", "=14", StringComparison.Ordinal));
            File.SetLastWriteTimeUtc(response, responseStamp);
            Assert.Equal("Input file bytes changed: " + response, SourceSnapshotVerifier.GetMismatch(first.inputs));
            await File.WriteAllTextAsync(response, responseText);
            File.SetLastWriteTimeUtc(response, responseStamp);
            string explicitMember = Path.Combine(explicitLibraries, "new-explicit-library.lib");
            await File.WriteAllTextAsync(explicitMember, "explicit membership");
            Assert.Equal("Include or library search membership changed: " + explicitLibraries, SourceSnapshotVerifier.GetMismatch(first.inputs));
            File.Delete(explicitMember);
            Assert.Null(SourceSnapshotVerifier.GetMismatch(first.inputs));

            // The ordinary build plan still freezes both implicit and requested
            // library roots and emits their ordered native linker arguments.
            // Full construction resolves the real SDK/linker independently of
            // the deliberately minimal preprocessing-only target above.
            NativeTargetDescriptor fullTarget = ToolchainResolver.ResolveToolchain(target, language, "clang");
            string scratch = Path.Combine(root, "full-build");
            NativeBuildPlan full = new CompilerBuildProvider("clang", compiler).Plan(new(fullTarget,
                [new(header, language, source.languageStandard)], scratch, Path.Combine(scratch, "library" + (OperatingSystem.IsWindows() ? ".dll" : ".so")))
            { argumentWorkingDirectory = root, libraryDirectories = [requestedLibraries] }, default);
            string[] libraryRoots = fullTarget.toolchain.libraryDirectories.Append(requestedLibraries).Distinct(StringComparer.Ordinal).ToArray();
            Assert.Contains(implicitLibraries, libraryRoots);
            Assert.Equal(libraryRoots, full.librarySearchDirectories.ToArray());
            NativeBuildCommand link = Assert.Single(full.commands, command => command.id == "link");
            if (OperatingSystem.IsWindows())
            {
                Assert.NotNull(fullTarget.toolchain.sdkVersion);
                Assert.NotNull(fullTarget.toolchain.linkerPath);
                Assert.True(File.Exists(fullTarget.toolchain.linkerPath));
                Assert.Equal(fullTarget.toolchain.linkerPath, link.executable);
                Assert.Equal(libraryRoots.Select(directory => "/LIBPATH:" + directory),
                    link.arguments.Where(argument => argument.StartsWith("/LIBPATH:", StringComparison.Ordinal)));
            }
            else
                foreach (string directory in libraryRoots) Assert.Contains(directory, link.arguments);
            Assert.False(File.Exists(full.outputFile));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task PreprocessingStillHashesItsActualOwnedCompilerBytes()
    {
        // The Windows LLVM executable is self-contained for this no-SDK input.
        // Never mutate the installed compiler or its shared library directory.
        if (!OperatingSystem.IsWindows()) return;
        string root = Path.Combine(Path.GetTempPath(), "bgcs preprocessing compiler bytes " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string compilerDirectory = Directory.CreateDirectory(Path.Combine(root, "compiler")).FullName;
            string compiler = Path.Combine(compilerDirectory, "clang.exe");
            File.Copy(InstalledClang(), compiler);
            string headers = Directory.CreateDirectory(Path.Combine(root, "headers")).FullName;
            string header = Path.Combine(headers, "original.h");
            await File.WriteAllTextAsync(header, "int source_view_compiler_bytes(void);\n");
            FrontendRequest source = new(Target(compiler), SourceLanguage.C, [header])
            { pathBase = root, arguments = ["-nostdinc"], parseMacros = false };
            NativeSourceViewResult view = await new NativeSourceViewPreprocessor().PrepareAsync(new(source, Path.Combine(root, "native")));
            Assert.False(File.Exists(Path.Combine(root, "native", "unused.o")));
            Assert.Contains("source_view_compiler_bytes", view.expandedSource);
            Assert.Contains(view.inputs.files, file => file.path == compiler);
            Assert.Null(SourceSnapshotVerifier.GetMismatch(view.inputs));
            DateTime stamp = File.GetLastWriteTimeUtc(compiler);
            using (FileStream file = new(compiler, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                file.Seek(-1, SeekOrigin.End);
                int last = file.ReadByte();
                Assert.InRange(last, 0, 255);
                file.Seek(-1, SeekOrigin.End);
                file.WriteByte((byte)(last ^ 1));
            }
            File.SetLastWriteTimeUtc(compiler, stamp);
            Assert.Equal("Input file bytes changed: " + compiler, SourceSnapshotVerifier.GetMismatch(view.inputs));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string InstalledClang()
        => NativeBuildToolDiscovery.FindExecutable("clang", OperatingSystem.IsWindows() ? "C:/Program Files/LLVM/bin/clang.exe" : null)
            ?? throw new InvalidOperationException("The preprocessing input-scope gate requires its actual installed Clang driver.");

    private static NativeTargetDescriptor Target(string compiler, params string[] implicitLibraries)
    {
        NativeTargetDescriptor identity = new TargetResolver().Resolve(new(new("host"), new(compilerPath: compiler)));
        return new(identity.targetId, identity.platformId, identity.architectureId, identity.abiId, identity.triple,
            new NativeToolchainDescriptor(compilerPath: compiler, libraryDirectories: implicitLibraries), identity.capabilities, identity.byteOrder);
    }
}
