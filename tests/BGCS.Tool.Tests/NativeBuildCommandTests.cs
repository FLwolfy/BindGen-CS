using System;
using System.IO;
using System.Text.Json;
using BGCS.Core.Targeting;
using BGCS.Cpp2C.Build;
using BGCS.CppAst.Parsing;
using BGCS.CppAst.Targeting;
using Xunit;

namespace BGCS.Tool.Tests;

public sealed class NativeBuildCommandTests
{
    [Fact]
    public void Run_DryRunJson_ProducesShellIndependentPlan()
    {
        using TestDirectory directory = new();
        CppBridgeBuildManifest manifest = CreateManifest();
        directory.WriteJson("bridge.manifest.json", manifest);
        string compiler = Environment.ProcessPath ?? throw new InvalidOperationException("Current process path is unavailable.");

        CommandResult result = Run(directory, "bridge.manifest.json", "--compiler", compiler, "--dry-run", "--json");

        Assert.Equal(0, result.ExitCode);
        using JsonDocument plan = JsonDocument.Parse(result.Output);
        Assert.Equal("clang-gnu-driver", plan.RootElement.GetProperty("provider").GetString());
        Assert.Equal(compiler, plan.RootElement.GetProperty("executable").GetString());
        Assert.Contains("-shared", plan.RootElement.GetProperty("arguments").ToString(), StringComparison.Ordinal);
        Assert.Contains("sample.cpp", plan.RootElement.GetProperty("arguments").ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Run_RelativeOutputPath_IsResolvedFromInvocationDirectory()
    {
        using TestDirectory directory = new();
        directory.WriteJson("bridge.manifest.json", CreateManifest());
        string compiler = Environment.ProcessPath ?? throw new InvalidOperationException("Current process path is unavailable.");

        CommandResult result = Run(directory, "bridge.manifest.json", "--compiler", compiler,
            "--output", "Consumer/native/sample.so", "--dry-run", "--json");

        Assert.Equal(0, result.ExitCode);
        using JsonDocument plan = JsonDocument.Parse(result.Output);
        Assert.Equal(System.IO.Path.Combine(directory.Path, "Consumer", "native", "sample.so"),
            plan.RootElement.GetProperty("outputFile").GetString());
    }

    [Fact]
    public void Run_IncompleteManifest_FailsBeforeCompilerExecution()
    {
        using TestDirectory directory = new();
        CppBridgeBuildManifest manifest = CreateManifest() with { libraryName = string.Empty };
        directory.WriteJson("bridge.manifest.json", manifest);

        CommandResult result = Run(directory, "bridge.manifest.json", "--dry-run");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("libraryName", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_WindowsAutoProvider_RespectsExplicitGnuDriver()
    {
        using TestDirectory directory = new();
        CppBridgeBuildManifest manifest = CreateManifest() with
        {
            targetIdentifier = "windows-x64-msvc",
            targetTriple = "x86_64-pc-windows-msvc"
        };
        directory.WriteJson("bridge.manifest.json", manifest);
        string compiler = CppToolchainDiscovery.FindCompiler(CppParserKind.Cpp)
            ?? throw new InvalidOperationException("A C++ compiler is required for provider selection.");

        CommandResult result = Run(directory, "bridge.manifest.json", "--compiler", compiler, "--dry-run", "--json");

        Assert.Equal(0, result.ExitCode);
        using JsonDocument plan = JsonDocument.Parse(result.Output);
        Assert.Equal("clang-gnu-driver", plan.RootElement.GetProperty("provider").GetString());
    }

    [Fact]
    public void Run_JsonBuild_EmitsOneMachineReadableResult()
    {
        using TestDirectory directory = new();
        string compiler = CppToolchainDiscovery.FindCompiler(CppParserKind.Cpp)
            ?? throw new InvalidOperationException("A host C++ compiler is required for the native-build CLI test.");
        NativeTargetDescriptor target = new ClangTargetResolver().Resolve(new(new NativeTargetId("host")));
        CppBridgeBuildManifest manifest = CreateManifest() with
        {
            targetIdentifier = target.targetId.value,
            targetTriple = target.triple,
            sourceFiles = ["sample.cpp"],
            publicHeaderFiles = [],
            originalHeaderFiles = [],
            includeDirectories = [],
            defines = []
        };
        directory.Write("sample.cpp", "extern \"C\" int bgcs_sample(void) { return 42; }\n");
        directory.WriteJson("bridge.manifest.json", manifest);

        string packageRoot = System.IO.Path.Combine(directory.Path, "package");
        CommandResult result = Run(directory, "bridge.manifest.json", "--compiler", compiler, "--json",
            "--package-root", packageRoot);

        Assert.Equal(0, result.ExitCode);
        using JsonDocument buildResult = JsonDocument.Parse(result.Output);
        Assert.True(buildResult.RootElement.GetProperty("success").GetBoolean());
        Assert.True(File.Exists(buildResult.RootElement.GetProperty("outputFile").GetString()));
        JsonElement packagedAsset = buildResult.RootElement.GetProperty("packagedAsset");
        Assert.Equal(NativeAssetLayout.GetRuntimeIdentifier(target.targetId.value),
            packagedAsset.GetProperty("runtimeIdentifier").GetString());
        Assert.True(File.Exists(packagedAsset.GetProperty("assetPath").GetString()));
    }

    private static CppBridgeBuildManifest CreateManifest() => new(
        "linux-x64-gnu",
        "x86_64-unknown-linux-gnu",
        null,
        null,
        "c++23",
        "sample",
        ["src/sample.cpp"],
        ["include/sample.h"],
        ["../sample.hpp"],
        ["include", ".."],
        [],
        ["SAMPLE=1"],
        [],
        [],
        [],
        []);

    private static CommandResult Run(TestDirectory directory, params string[] args)
    {
        using StringWriter output = new();
        using StringWriter error = new();
        int exitCode = CliInvocation.Run(["native-build", .. args], directory.Path, output, error);
        return new(exitCode, output.ToString(), error.ToString());
    }

    private sealed record CommandResult(int ExitCode, string Output, string Error);

    private sealed class TestDirectory : IDisposable
    {
        public TestDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bgcs-native-build-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void WriteJson<T>(string relativePath, T value) =>
            File.WriteAllText(System.IO.Path.Combine(Path, relativePath), JsonSerializer.Serialize(value));

        public void Write(string relativePath, string value) =>
            File.WriteAllText(System.IO.Path.Combine(Path, relativePath), value);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, true);
        }
    }
}
