using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.Contracts.Ports;
using BGCS.NativeBuild.Execution;
using BGCS.NativeBuild.Planning;
using BGCS.Platforms.Targeting;
using BGCS.Platforms.Toolchains;
using BGCS.Infrastructure.IO;
using Xunit;

namespace BGCS.NativeBuild.Tests;

public sealed class NativeProviderExecutionTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int RoundTrip(int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint TextValue();

    [Theory]
    [InlineData("msvc")]
    [InlineData("clang")]
    [InlineData("clang-cl")]
    [InlineData("cmake")]
    [InlineData("meson")]
    [InlineData("msbuild")]
    [Trait("Category", "WindowsNativeProvider")]
    public async Task WindowsProvidersCompileMixedC17Cpp23AndInvoke(string providerId)
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64) return;
        string? parentPythonUtf8 = Environment.GetEnvironmentVariable("PYTHONUTF8");
        string? parentPythonEncoding = Environment.GetEnvironmentVariable("PYTHONIOENCODING");
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs provider 验收 & 50% " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        nint module = 0;
        try
        {
            string c = Path.Combine(temporary, "C source & test.c"), cpp = Path.Combine(temporary, "C++ source ' test.cpp");
            string dependency = Path.Combine(temporary, "native only header.hpp");
            string argumentDirectory = Path.Combine(temporary, "arguments 目录 & 50%");
            Directory.CreateDirectory(argumentDirectory);
            string response = Path.Combine(argumentDirectory, "driver.rsp");
            await File.WriteAllTextAsync(response, providerId == "clang" ? "-DNATIVE_ARGUMENT_FACTOR=13\n" : "/DNATIVE_ARGUMENT_FACTOR=13\n");
            await File.WriteAllTextAsync(Path.Combine(argumentDirectory, "native_argument_header.h"), "#define NATIVE_ARGUMENT_HEADER 17\n");
            await File.WriteAllTextAsync(dependency, "inline constexpr int native_increment = 7;\nextern \"C\" __declspec(dllexport) int native_single_header_definition(int value) { return value + native_increment; }\n");
            string facts = Path.Combine(temporary, "facts.input.hpp");
            await File.WriteAllTextAsync(facts, new NativeFactInputEmitter().Emit([dependency]));
            await File.WriteAllTextAsync(c, "#include <stdint.h>\n#include \"native_argument_header.h\"\n_Static_assert(__STDC_VERSION__ >= 201710L, \"C17\");\n_Static_assert(NATIVE_ARGUMENT_HEADER == 17, \"trailing-backslash include directory\");\n_Static_assert(NATIVE_ARGUMENT_FACTOR == 13, \"relative driver response\");\n__declspec(dllexport) const char* native_quoted(void) { return NATIVE_ARGUMENT_TEXT; }\n__declspec(dllexport) int native_roundtrip(int value) { return value * 2; }\n");
            await File.WriteAllTextAsync(cpp, "#include <vector>\n#include \"native only header.hpp\"\nstatic_assert(_MSVC_LANG > 202002L, \"C++23\");\nextern \"C\" __declspec(dllexport) int native_cpp_roundtrip(int value) { std::vector<int> data{value, native_increment}; return data[0] + data[1]; }\n");
            string cl = MsvcToolchainDiscovery.FindCompiler("x64") ?? throw new InvalidOperationException("Visual C++ is required by the Windows provider gate.");
            string compiler = providerId == "clang" ? ToolchainDiscovery.FindCompiler(SourceLanguage.Cpp) ?? throw new InvalidOperationException("Clang is required by the Windows provider gate.")
                : providerId == "clang-cl" ? NativeBuildToolDiscovery.FindClangCl() ?? throw new InvalidOperationException("clang-cl is required by the Windows provider gate.") : cl;
            NativeTargetDescriptor target = ToolchainResolver.ResolveToolchain(new TargetResolver().Resolve(new(new("windows-x64-msvc"), new(compiler))), SourceLanguage.Cpp);
            string scratch = Path.Combine(temporary, "scratch");
            Directory.CreateDirectory(scratch);
            NativeBuildRequest request = new(target, [new(c, SourceLanguage.C, "c17"), new(cpp, SourceLanguage.Cpp, "c++23")], scratch, Path.Combine(scratch, "provider.dll"))
            {
                argumentWorkingDirectory = argumentDirectory,
                preprocessingSources = [new(facts, SourceLanguage.Cpp, "c++23")],
                includeDirectories = [argumentDirectory + Path.DirectorySeparatorChar],
                defines = ["NATIVE_ARGUMENT_TEXT=\"A\\042B\\134C&50%目录\""],
                compilerArguments = ["@driver.rsp"]
            };
            var provider = new NativeBuildProviderResolver().Resolve(target, providerId);
            NativeBuildPlan plan = provider.Plan(request, CancellationToken.None);
            if (providerId == "meson")
            {
                Assert.All(plan.commands, command =>
                {
                    Assert.Equal("1", command.environmentOverrides["PYTHONUTF8"]);
                    Assert.Equal("utf-8", command.environmentOverrides["PYTHONIOENCODING"]);
                    Assert.Equal("utf-8", command.outputEncoding);
                    Assert.Null(command.environmentOverrides["INCLUDE"]);
                });
                // Native construction receipts persist this same complete plan;
                // encoding belongs to its identity, not the invoking test shell.
                JsonElement frozen = JsonSerializer.SerializeToElement(plan);
                Assert.All(frozen.GetProperty("commands").EnumerateArray(), command =>
                {
                    JsonElement environment = command.GetProperty("environmentOverrides");
                    Assert.Equal("1", environment.GetProperty("PYTHONUTF8").GetString());
                    Assert.Equal("utf-8", environment.GetProperty("PYTHONIOENCODING").GetString());
                    Assert.Equal("utf-8", command.GetProperty("outputEncoding").GetString());
                });
            }
            else Assert.All(plan.commands, command => Assert.Null(command.outputEncoding));
            Assert.Equal(2, plan.dependencyFiles.Length);
            Assert.Equal(facts, Assert.Single(plan.preprocessingOperations, operation => operation.preprocessingOnly).source);
            Assert.DoesNotContain(plan.commands, command => command.inputFiles.Contains(facts));
            ImmutableArray<NativeToolPrerequisite> prerequisites = Assert.IsAssignableFrom<INativeBuildPrerequisiteProvider>(provider).DescribePrerequisites();
            Assert.Equal(plan.compilerPath, Assert.Single(prerequisites, tool => tool.role == "compiler").path);
            Assert.All(prerequisites.Where(tool => tool.requiredOutputKind is null or NativeOutputKind.SharedLibrary), tool =>
            { Assert.NotNull(tool.path); Assert.True(Path.IsPathFullyQualified(tool.path!)); Assert.True(File.Exists(tool.path), tool.path); });
            Assert.False(File.Exists(plan.outputFile));
            Assert.All(plan.generatedInputFiles, input => Assert.False(File.Exists(Path.Combine(scratch, input.relativePath))));
            int verifiedSources = 0;
            NativeBuildExecutionResult result = await new PlannedNativeBuildExecutor().ExecuteWithDependenciesAsync(plan, TimeSpan.FromMinutes(3),
                sourceVerifier: async (operation, token) =>
                {
                    string text = await File.ReadAllTextAsync(operation.outputFile, token);
                    Assert.Contains(operation.source == c ? "native_roundtrip" : "native_cpp_roundtrip", text);
                    Assert.DoesNotContain("#include \"native only header.hpp\"", text);
                    Assert.False(NativeEffectiveFactArguments.Resolve(plan, operation, token).IsDefault);
                    verifiedSources++;
                }, verificationSources: [c, cpp]);
            Assert.Equal(2, verifiedSources);
            Assert.All(result.executions, operation => Assert.Equal(0, operation.exitCode));
            if (providerId == "meson")
            {
                Assert.True(result.executions[^1].standardOutput.Contains("验收", StringComparison.Ordinal),
                    "Meson install stdout must preserve the actual Unicode output path. Full captured stdout JSON:\n"
                    + JsonSerializer.Serialize(result.executions[^1].standardOutput) + "\nFull captured stderr JSON:\n"
                    + JsonSerializer.Serialize(result.executions[^1].standardError));
                Assert.Equal(parentPythonUtf8, Environment.GetEnvironmentVariable("PYTHONUTF8"));
                Assert.Equal(parentPythonEncoding, Environment.GetEnvironmentVariable("PYTHONIOENCODING"));
            }
            Assert.Contains(result.inputSnapshot.files, input => string.Equals(input.path, dependency, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
            Assert.Contains(result.inputSnapshot.files, input => string.Equals(input.path, facts, StringComparison.OrdinalIgnoreCase));
            Assert.True(SourceSnapshotVerifier.Matches(result.inputSnapshot));
            Assert.Contains(result.argumentInputs.files, file => file.path == response);
            module = NativeLibrary.Load(plan.outputFile);
            Assert.Equal(42, Marshal.GetDelegateForFunctionPointer<RoundTrip>(NativeLibrary.GetExport(module, "native_roundtrip"))(21));
            Assert.Equal(42, Marshal.GetDelegateForFunctionPointer<RoundTrip>(NativeLibrary.GetExport(module, "native_cpp_roundtrip"))(35));
            Assert.Equal(42, Marshal.GetDelegateForFunctionPointer<RoundTrip>(NativeLibrary.GetExport(module, "native_single_header_definition"))(35));
            Assert.Equal("A\"B\\C&50%目录", Marshal.PtrToStringUTF8(Marshal.GetDelegateForFunctionPointer<TextValue>(NativeLibrary.GetExport(module, "native_quoted"))()));
            await File.WriteAllTextAsync(dependency, "inline constexpr int native_increment = 8;\n");
            Assert.False(SourceSnapshotVerifier.Matches(result.inputSnapshot));
        }
        finally
        {
            if (module != 0) NativeLibrary.Free(module);
            Directory.Delete(temporary, recursive: true);
        }
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("sub/../../outside.txt")]
    [InlineData("sub\\outside.txt")]
    public async Task GeneratedBuildInputsCannotEscapeScratch(string relativePath)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-provider-input-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            NativeTargetDescriptor target = new TargetResolver().Resolve(new(new("host")));
            NativeBuildPlan plan = new("fault", target, [new("fault", "does-not-exist", [], temporary)], Path.Combine(temporary, "missing"))
            { scratchDirectory = temporary, generatedInputFiles = [new(relativePath, "never write outside scratch")] };
            await Assert.ThrowsAsync<ArgumentException>(() => new PlannedNativeBuildExecutor().ExecuteAsync(plan, TimeSpan.FromSeconds(10)));
            Assert.Empty(Directory.GetFiles(temporary, "*", SearchOption.AllDirectories));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Fact]
    public void MissingExplicitArchiveOverrideCannotFallBackAndInspectionDoesNotCreatePlans()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-native-prerequisites-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            NativeTargetDescriptor target = new TargetResolver().Resolve(new(new("host")));
            string missing = Path.Combine(temporary, "missing-archive-tool");
            Assert.Throws<FileNotFoundException>(() => new NativeBuildProviderResolver().Resolve(target, "auto", auxiliaryToolPath: missing));
            CompilerBuildProvider unconfiguredArchive = new("clang", Path.Combine(temporary, "compiler"));
            ImmutableArray<NativeToolPrerequisite> prerequisites = unconfiguredArchive.DescribePrerequisites();
            NativeToolPrerequisite archive = Assert.Single(prerequisites, tool => tool.role == "archive");
            Assert.Null(archive.path);
            Assert.Equal(NativeOutputKind.StaticLibrary, archive.requiredOutputKind);
            Assert.Empty(Directory.GetFileSystemEntries(temporary));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Fact]
    public void MissingExplicitBuildToolDoesNotUseInstalledFallback()
    {
        NativeTargetDescriptor target = new TargetResolver().Resolve(new(new("host")));
        string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "cmake");
        Assert.Throws<FileNotFoundException>(() => new NativeBuildProviderResolver().Resolve(target, "cmake", missing));
    }
}
