using System;
using System.IO;
using System.Threading.Tasks;
using BGCS.Contracts.Requests;
using BGCS.Infrastructure.Execution;
using BGCS.NativeBuild.Validation;
using BGCS.Platforms.Targeting;
using Xunit;

namespace BGCS.NativeBuild.Tests;

public sealed class MachOExportTests
{
    [Theory]
    [InlineData(NativeOutputKind.Object)]
    [InlineData(NativeOutputKind.StaticLibrary)]
    public async Task RealMachOSymbolInspectionPreservesTheDeclaredLeadingUnderscore(NativeOutputKind kind)
    {
        string compiler = FindLlvmTool("clang"), inspector = FindLlvmTool("llvm-nm"), archiver = FindLlvmTool("llvm-ar");
        string root = Path.Combine(Path.GetTempPath(), "bgcs Mach-O symbols 验收 & " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "exports.c"), objectFile = Path.Combine(root, "exports.o"), archive = Path.Combine(root, "exports.a");
            await File.WriteAllTextAsync(source, "int native_export(int value) { return value + 1; }\nint _native_export(int value) { return value + 2; }\n");
            ProcessExecutionResult compiled = await ProcessExecutor.ExecuteAsync(compiler,
                ["--no-default-config", "--target=x86_64-apple-darwin", "-std=c17", "-c", source, "-o", objectFile], root, TimeSpan.FromMinutes(1));
            Assert.False(compiled.timedOut);
            Assert.True(compiled.exitCode == 0, compiled.standardError);
            string artifact = objectFile;
            if (kind == NativeOutputKind.StaticLibrary)
            {
                ProcessExecutionResult archived = await ProcessExecutor.ExecuteAsync(archiver, ["rcs", archive, objectFile], root, TimeSpan.FromMinutes(1));
                Assert.False(archived.timedOut);
                Assert.True(archived.exitCode == 0, archived.standardError);
                artifact = archive;
            }
            var target = new TargetResolver().Resolve(new(new("macos-x64-darwin")));
            NativeArtifactVerifier.Verify(artifact, target, kind);
            var exports = await NativeExportInspector.InspectAsync(target, artifact, ["native_export", "_native_export"], toolPath: inspector, outputKind: kind);
            Assert.True(exports.success, string.Join(Environment.NewLine, exports.missing));
            Assert.Contains("native_export", exports.actual);
            Assert.Contains("_native_export", exports.actual);
            if (OperatingSystem.IsMacOS())
            {
                var appleExports = await NativeExportInspector.InspectAsync(target, artifact, ["native_export", "_native_export"], toolPath: "/usr/bin/nm", outputKind: kind);
                Assert.True(appleExports.success, string.Join(Environment.NewLine, appleExports.missing));
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string FindLlvmTool(string name)
    {
        string? selected = NativeBuildToolDiscovery.FindExecutable(name,
            OperatingSystem.IsWindows() ? Path.Combine("C:/Program Files/LLVM/bin", name + ".exe") : null);
        if (selected is not null) return selected;
        if (OperatingSystem.IsMacOS())
            foreach (string prefix in new[] { "/usr/local/opt/llvm@20/bin", "/opt/homebrew/opt/llvm@20/bin" })
                if (NativeBuildToolDiscovery.FindExecutable(Path.Combine(prefix, name)) is string installed) return installed;
        if (OperatingSystem.IsLinux())
            foreach (int version in new[] { 20, 19, 18, 17, 16, 15, 14 })
                if (NativeBuildToolDiscovery.FindExecutable(name + "-" + version.ToString(System.Globalization.CultureInfo.InvariantCulture)) is string installed) return installed;
        throw new InvalidOperationException("The real Mach-O format gate requires the explicitly selected LLVM tool: " + name);
    }
}
