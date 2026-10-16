using System;
using BGCS.Contracts.Inputs;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.Infrastructure.IO;
using BGCS.NativeBuild.Execution;
using BGCS.NativeBuild.Planning;
using BGCS.NativeBuild.Validation;
using BGCS.Platforms;
using BGCS.Platforms.Assets;
using BGCS.Platforms.Toolchains;
using Xunit;

namespace BGCS.NativeBuild.Tests;

public sealed class WasmNativeExecutionTests
{
    [Theory]
    [InlineData(NativeOutputKind.StaticLibrary)]
    [InlineData(NativeOutputKind.Object)]
    [Trait("Category", "WasmNativeProvider")]
    public async Task WorkloadCompilerBuildsMixedC17Cpp23WithIndependentDependenciesAndExports(NativeOutputKind kind)
    {
        (string sdk, string cache)? installed = InstalledWorkload();
        if (installed is null)
        {
            Assert.NotEqual("1", Environment.GetEnvironmentVariable("BGCS_REQUIRE_WASM_NATIVE"));
            return;
        }
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-wasm-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            NativeTargetDescriptor target = EmscriptenSdkDiscovery.ResolveWorkload(installed.Value.sdk, installed.Value.cache);
            string c = Path.Combine(temporary, "native.c"), cpp = Path.Combine(temporary, "native.cpp"), header = Path.Combine(temporary, "factor.h");
            await File.WriteAllTextAsync(header, "#define NATIVE_FACTOR 2\n");
            await File.WriteAllTextAsync(c, "#include \"factor.h\"\n_Static_assert(sizeof(void*) == 4, \"wasm32 pointer width\");\nint native_c_roundtrip(int value) { return value * NATIVE_FACTOR; }\n");
            await File.WriteAllTextAsync(cpp, "#include <vector>\nstatic_assert(sizeof(void*) == 4);\nextern \"C\" int native_cpp_roundtrip(int value) { std::vector<int> values{value, 7}; return values[0] + values[1]; }\n");
            string scratch = Path.Combine(temporary, "scratch");
            Directory.CreateDirectory(scratch);
            NativeBuildRequest request = new(target, [new(c, SourceLanguage.C, "c17"), new(cpp, SourceLanguage.Cpp, "c++23")], scratch,
                Path.Combine(scratch, NativeOutputLayout.ResolveFilename("fixture", target, kind))) { outputKind = kind, argumentWorkingDirectory = temporary };
            NativeBuildPlan plan = new NativeBuildProviderResolver().Resolve(target, "clang").Plan(request, CancellationToken.None);
            Assert.DoesNotContain(plan.commands[0].arguments, argument => argument == target.toolchain.cxxSystemIncludeFolders.Single());
            NativeBuildExecutionResult built = await new PlannedNativeBuildExecutor().ExecuteWithDependenciesAsync(plan, TimeSpan.FromMinutes(3));
            NativeArtifactVerifier.Verify(plan.outputFile, target, kind);
            string exportTool = Path.Combine(Path.GetDirectoryName(target.toolchain.compilerPath!)!, OperatingSystem.IsWindows() ? "llvm-nm.exe" : "llvm-nm");
            var exports = await NativeExportInspector.InspectAsync(target, plan.outputFile, ["native_c_roundtrip", "native_cpp_roundtrip"], toolPath: exportTool, outputKind: kind);
            Assert.True(exports.success);
            Assert.All(built.executions, operation => Assert.Equal(0, operation.exitCode));
            Assert.Contains(built.inputSnapshot.files, input => input.path == header);
            Assert.True(SourceSnapshotVerifier.Matches(built.inputSnapshot));
            ArtifactDescriptor artifact = new("target/native/bin/" + Path.GetFileName(plan.outputFile), "application/octet-stream", new(Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(plan.outputFile)))));
            NativeReferenceDescriptor reference = NativeReferenceCatalog.Create(target, ManagedRuntimeResolver.Resolve(target, ManagedRuntimeKind.BrowserWasm, kind), kind, artifact, "fixture");
            Assert.Equal(NativeReferenceKind.BrowserNativeFile, reference.kind);
            Assert.Equal(Path.GetFileNameWithoutExtension(plan.outputFile), reference.importModuleName);
            await File.AppendAllTextAsync(header, "// actual compiler input changed\n");
            Assert.False(SourceSnapshotVerifier.Matches(built.inputSnapshot));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    private static (string sdk, string cache)? InstalledWorkload()
    {
        string? sdk = Environment.GetEnvironmentVariable("BGCS_WASM_SDK_TOOLS_ROOT"), cache = Environment.GetEnvironmentVariable("BGCS_WASM_CACHE_TOOLS_ROOT");
        if (sdk is not null || cache is not null)
        {
            if (sdk is null || cache is null) throw new InvalidOperationException("The native Wasm gate requires both exact SDK and cache tools roots.");
            return (sdk, cache);
        }
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "BindGen-CS.slnx"))) repository = repository.Parent;
        if (repository is null) return null;
        string rid = OperatingSystem.IsWindows() ? "win-x64" : OperatingSystem.IsMacOS() ? "osx-x64" : "linux-x64";
        string packs = Path.Combine(repository.FullName, "artifacts", "toolchains", "dotnet", "packs");
        sdk = Path.Combine(packs, "Microsoft.NET.Runtime.Emscripten.3.1.56.Sdk." + rid, "10.0.12", "tools");
        cache = Path.Combine(packs, "Microsoft.NET.Runtime.Emscripten.3.1.56.Cache." + rid, "10.0.12", "tools");
        return Directory.Exists(sdk) && Directory.Exists(cache) ? (sdk, cache) : null;
    }
}
