using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using BGCS.Configuration;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Managed;
using Xunit;

namespace BGCS.Tool.Tests;

public sealed class BridgeCommandTests : IDisposable
{
    private readonly string m_root = Path.Combine(Path.GetTempPath(), "bgcs-bridge-cli-" + Guid.NewGuid().ToString("N"));
    private static readonly JsonSerializerOptions s_options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    public BridgeCommandTests()
    {
        Directory.CreateDirectory(m_root);
        File.WriteAllText(Path.Combine(m_root, "api.hpp"), "class Counter { public: int Value() const { return 7; } };\n");
    }

    [Fact]
    public void InvalidManagedContributionPreservesPublishedManagedAndNativeArtifacts()
    {
        BindingConfiguration valid = CreateConfig("host", "int32_t");
        Write(valid);
        Assert.Equal(0, Invoke("generate").ExitCode);
        string published = Path.Combine(m_root, "Generated");
        var previous = Directory.GetFiles(published, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        Assert.Contains(previous.Keys, path => path.EndsWith(".cpp", StringComparison.Ordinal));
        Assert.Contains(previous.Keys, path => path.EndsWith(".cs", StringComparison.Ordinal));
        BindingConfiguration invalid = valid with { cpp = valid.cpp with
        { typeLowerings = [valid.cpp.typeLowerings[0] with { managedProjection = new("uint", "{value} +", "checked((uint){value})") }] } };
        Write(invalid);

        var result = Invoke("generate");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("syntactically invalid", result.Error, StringComparison.Ordinal);
        Assert.Equal(previous.Keys.Order(StringComparer.Ordinal), Directory.GetFiles(published, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
        foreach (var item in previous) Assert.Equal(item.Value, File.ReadAllBytes(item.Key));
        Assert.Empty(Directory.GetDirectories(m_root, ".bgcs-staging-*"));
    }

    [Theory]
    [InlineData("windows-x64-msvc", "ulong")]
    [InlineData("emscripten-wasm32-emscripten", "uint")]
    public void ManagedProjectionUsesTheParsedTargetAbi(string target, string expectedCarrier)
    {
        Write(CreateConfig(target, "size_t"));

        var result = Invoke("generate");

        Assert.True(result.ExitCode == 0, result.Output + Environment.NewLine + result.Error);
        string projections = File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(m_root, "Generated"), "ConfiguredLoweringProjections.g.cs", SearchOption.AllDirectories)));
        Assert.Contains($"{expectedCarrier} ToNative_count", projections, StringComparison.Ordinal);
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(m_root, "Generated"), "*.h", SearchOption.AllDirectories));
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(m_root, "Generated"), "*.cs", SearchOption.AllDirectories));
    }

    [Fact]
    public void BridgeCommandPublishesOnlyNativeAndMetadataArtifacts()
    {
        BindingConfiguration config = CreateConfig("host", "int32_t");
        Write(config);

        var result = Invoke("bridge");

        Assert.True(result.ExitCode == 0, result.Output + Environment.NewLine + result.Error);
        Assert.Empty(Directory.GetFiles(Path.Combine(m_root, "Generated"), "*.cs", SearchOption.AllDirectories));
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(m_root, "Generated"), "*.cpp", SearchOption.AllDirectories));
        Assert.Single(Directory.GetFiles(Path.Combine(m_root, "Generated"), "bridge.manifest.json", SearchOption.AllDirectories));
    }

    public void Dispose() => Directory.Delete(m_root, true);

    private void Write(BindingConfiguration config) => File.WriteAllText(Path.Combine(m_root, "bindgen.json"), JsonSerializer.Serialize(config, s_options));
    private (int ExitCode, string Output, string Error) Invoke(string command)
    {
        using StringWriter output = new();
        using StringWriter error = new();
        int code = CliInvocation.Run([command, "bindgen.json"], m_root, output, error);
        return (code, output.ToString(), error.ToString());
    }
    private static BindingConfiguration CreateConfig(string target, string carrier) => new()
    {
        schemaVersion = 1,
        library = new() { id = "fixture", nativeName = "fixture" },
        source = new() { language = SourceLanguage.Cpp, headers = ["api.hpp"] },
        api = new() { name = "Bridge", @namespace = "Fixture.Generated" },
        targets = [new() { id = target }],
        output = new() { root = "Generated" },
        cpp = new() { typeLowerings = [new TypeLoweringRecipe
        { id = "count", typePattern = "Unused::Count", cAbiType = carrier, managedProjection = new("uint", "{value}", "checked((uint){value})") }] }
    };
}
