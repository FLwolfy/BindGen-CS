using System;
using System.IO;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Targets;
using BGCS.Contracts.Common;
using BGCS.Contracts.Requests;
using BGCS.Platforms.Targeting;
using Xunit;

namespace BGCS.Tool.Tests;

public sealed class NativeBuildCommandTests
{
    [Theory]
    [InlineData("vendor.custom")]
    [InlineData("vendor/custom")]
    [InlineData("1vendor.custom")]
    public void Run_CustomProviderId_ReachesHostServiceResolutionAndReportsUnavailableService(string identity)
    {
        using TestDirectory directory = new();
        directory.WriteManifest(CreateManifest(directory));

        CommandResult result = Run(directory, "bridge.manifest.json", "--provider", identity, "--dry-run", "--json");

        Assert.Equal(1, result.ExitCode);
        using JsonDocument report = JsonDocument.Parse(result.Output);
        Assert.False(report.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(identity, result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../clang")]
    [InlineData("provider with spaces")]
    public void Run_InvalidProviderIdentity_IsRejectedBeforeManifestExecution(string identity)
    {
        using TestDirectory directory = new();
        directory.WriteManifest(CreateManifest(directory));

        CommandResult result = Run(directory, "bridge.manifest.json", "--provider", identity, "--dry-run", "--json");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("stable service ID", result.Error, StringComparison.Ordinal);
        Assert.Empty(result.Output);
    }

    [Fact]
    public void Run_DryRunJson_ProducesShellIndependentImmutablePlan()
    {
        using TestDirectory directory = new();
        NativeBuildManifest manifest = CreateManifest(directory);
        directory.WriteManifest(manifest);
        string compiler = FindCompiler();
        CommandResult result = Run(directory, "bridge.manifest.json", "--compiler", compiler, "--dry-run", "--json");
        Assert.True(result.ExitCode == 0, result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        JsonElement plan = document.RootElement.GetProperty("plan");
        Assert.Equal(compiler, plan.GetProperty("compilerPath").GetString());
        Assert.Contains("sample.cpp", plan.GetProperty("commands").ToString());
        foreach (JsonElement command in plan.GetProperty("commands").EnumerateArray())
            Assert.Equal(JsonValueKind.Array, command.GetProperty("arguments").ValueKind);
    }

    [Fact]
    public void Run_RelativeOutputPath_IsResolvedFromInvocationDirectory()
    {
        using TestDirectory directory = new();
        directory.WriteManifest(CreateManifest(directory));
        string filename = OperatingSystem.IsWindows() ? "sample.dll" : OperatingSystem.IsMacOS() ? "libsample.dylib" : "libsample.so";
        string output = Path.Combine("Consumer native", filename);
        CommandResult result = Run(directory, "bridge.manifest.json", "--compiler", FindCompiler(), "--output", output, "--dry-run", "--json");
        Assert.True(result.ExitCode == 0, result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        string planned = document.RootElement.GetProperty("nativeArtifact").GetString()!;
        Assert.Equal(Path.GetFullPath(output, directory.Path), planned);
        Assert.False(File.Exists(planned));
    }

    [Fact]
    public void Run_IncompleteManifest_FailsBeforeCompilerExecution()
    {
        using TestDirectory directory = new();
        directory.WriteManifest(CreateManifest(directory) with { libraryName = string.Empty });
        CommandResult result = Run(directory, "bridge.manifest.json", "--dry-run");
        Assert.NotEqual(0, result.ExitCode);
        Assert.NotEmpty(result.Error);
        Assert.Empty(Directory.GetFiles(directory.Path, "*.dll", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.so", SearchOption.AllDirectories));
    }

    [Fact]
    public void Run_WindowsAutoProvider_RespectsExplicitClangGnuSyntaxDriver()
    {
        using TestDirectory directory = new();
        directory.WriteManifest(CreateManifest(directory, "windows-x64-msvc"));
        string driver = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LLVM", "bin", "clang++.exe")
            : "clang++";
        string compiler = ToolchainDiscovery.FindCompiler(SourceLanguage.Cpp, driver)
            ?? throw new InvalidOperationException("The launch desktop matrix requires a Clang driver for cross-target plan verification.");
        CommandResult result = Run(directory, "bridge.manifest.json", "--compiler", compiler, "--dry-run", "--json");
        Assert.True(result.ExitCode == 0, result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        Assert.Equal("clang", document.RootElement.GetProperty("plan").GetProperty("providerId").GetString());
    }

    [Fact]
    public void Run_JsonBuild_EmitsOneResultAndStagesVerifiedNativeRidAssets()
    {
        using TestDirectory directory = new();
        NativeBuildManifest manifest = CreateManifest(directory);
        directory.WriteManifest(manifest);
        string package = Path.Combine(directory.Path, "package");
        CommandResult result = Run(directory, "bridge.manifest.json", "--compiler", FindCompiler(), "--json", "--package-root", package);
        Assert.True(result.ExitCode == 0, result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.True(File.Exists(document.RootElement.GetProperty("nativeArtifact").GetString()));
        Assert.True(document.RootElement.GetProperty("exportEvidence").GetProperty("success").GetBoolean());
        string[] binaries = Directory.GetFiles(Path.Combine(package, "runtimes"), "*", SearchOption.AllDirectories);
        Assert.Single(binaries);
        Assert.True(File.Exists(binaries[0]));
    }

    [Fact]
    public void ChangedSourceContentLock_IsRejectedBeforeNativeExecution()
    {
        using TestDirectory directory = new();
        directory.WriteManifest(CreateManifest(directory));
        File.AppendAllText(Path.Combine(directory.Path, "sample.cpp"), "// changed after manifest\n");
        CommandResult result = Run(directory, "bridge.manifest.json", "--dry-run", "--json");
        Assert.NotEqual(0, result.ExitCode);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
    }

    private static string FindCompiler() => ToolchainDiscovery.FindCompiler(SourceLanguage.Cpp)
        ?? throw new InvalidOperationException("A host C++ compiler is required for native CLI verification.");

    private static NativeBuildManifest CreateManifest(TestDirectory directory, string targetId = "host")
    {
        string source = Path.Combine(directory.Path, "sample.cpp");
        File.WriteAllText(source, """
            #if defined(_WIN32)
            #define EXPORT __declspec(dllexport)
            #else
            #define EXPORT __attribute__((visibility("default")))
            #endif
            extern "C" EXPORT int bgcs_sample(void) { return 42; }
            """, Encoding.UTF8);
        byte[] bytes = File.ReadAllBytes(source);
        NativeTargetDescriptor target = new TargetResolver().Resolve(new(new NativeTargetId(targetId)));
        return new("sample", "sample", target, [new(source, SourceLanguage.Cpp, "c++23")], [],
            [new(source, source, bytes.LongLength, new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(bytes))))], ["bgcs_sample"]);
    }

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
        internal TestDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bgcs-native-build-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        internal string Path { get; }
        internal void WriteManifest(NativeBuildManifest manifest) => File.WriteAllText(System.IO.Path.Combine(Path, "bridge.manifest.json"), JsonSerializer.Serialize(manifest,
            new JsonSerializerOptions { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) } }));
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
