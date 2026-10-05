using System;
using System.IO;
using BGCS.Cpp2C.Configuration;
using Xunit;

namespace BGCS.Tool.Tests;

public sealed class BridgeCommandTests : IDisposable
{
    private readonly string m_root = Path.Combine(Path.GetTempPath(), "bgcs-bridge-cli-" + Guid.NewGuid().ToString("N"));

    public BridgeCommandTests()
    {
        Directory.CreateDirectory(m_root);
        File.WriteAllText(Path.Combine(m_root, "api.hpp"), "class Counter { public: int Value() const { return 7; } };\n");
    }

    [Fact]
    public void InvalidManagedContributionPreservesBothPublishedOutputTrees()
    {
        Cpp2CGeneratorConfig config = CreateConfig("host", "int32_t");
        config.typeLowerings[0].managedProjection = new("uint", "{value} +", "checked((uint){value})");
        config.Save(Path.Combine(m_root, "bridge.json"));
        string native = CreatePrevious("Native");
        string managed = CreatePrevious("Generated");
        using StringWriter output = new();
        using StringWriter error = new();

        int result = CliInvocation.Run(["bridge", "bridge.json"], m_root, output, error);

        Assert.NotEqual(0, result);
        Assert.Contains("syntactically invalid", error.ToString(), StringComparison.Ordinal);
        Assert.Equal("previous", File.ReadAllText(Path.Combine(native, "previous.txt")));
        Assert.Equal("previous", File.ReadAllText(Path.Combine(managed, "previous.txt")));
        Assert.Single(Directory.GetFiles(native));
        Assert.Single(Directory.GetFiles(managed));
        Assert.Empty(Directory.GetDirectories(m_root, ".bgcs-staging-*"));
    }

    [Theory]
    [InlineData("windows-x64-msvc", "ulong")]
    [InlineData("emscripten-wasm32-emscripten", "uint")]
    public void ManagedProjectionUsesTheParsedTargetAbi(
        string target,
        string expectedCarrier
    ) {
        Cpp2CGeneratorConfig config = CreateConfig(target, "size_t");
        config.Save(Path.Combine(m_root, "bridge.json"));
        using StringWriter output = new();
        using StringWriter error = new();

        int result = CliInvocation.Run(["bridge", "bridge.json"], m_root, output, error);

        Assert.True(result == 0, output + Environment.NewLine + error);
        string projections = File.ReadAllText(Path.Combine(m_root, "Generated", "Extensions", "ConfiguredLoweringProjections.g.cs"));
        Assert.Contains($"{expectedCarrier} ToNative_count", projections, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(m_root, "Native", "include", "extensions", "managed_projection_types.h")));
        Assert.True(File.Exists(Path.Combine(m_root, "Generated", "Bindings.cs")));
    }

    public void Dispose() => Directory.Delete(m_root, true);

    private static Cpp2CGeneratorConfig CreateConfig(
        string target,
        string carrier
    ) {
        Cpp2CGeneratorConfig config = new()
        {
            targetId = target,
            generateCSharpBindings = true,
            outputPath = "Native",
            cSharpOutputPath = "Generated",
            cSharpApiName = "Bridge",
            cSharpNamespace = "Fixture.Generated",
            nativeLibraryName = "fixture"
        };
        config.entryFiles.Add("api.hpp");
        config.typeLowerings.Add(new()
        {
            name = "count",
            typePattern = "Unused::Count",
            cAbiType = carrier,
            managedProjection = new("uint", "{value}", "checked((uint){value})")
        });
        return config;
    }

    private string CreatePrevious(string directory)
    {
        string path = Path.Combine(m_root, directory);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "previous.txt"), "previous");
        return path;
    }
}
