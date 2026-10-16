using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace BGCS.Tool.Tests;

public sealed class InitCommandTests
{
    [Theory]
    [InlineData("c", "native.h", "int native_add(int left, int right);", "bindgen.json", "generate")]
    [InlineData("cpp", "native.hpp", "class Widget { public: int Add(int value); };", "bindgen.json", "generate")]
    public void Run_InitializedConfiguration_CanGenerateWithoutEditing(
        string language,
        string header,
        string source,
        string config,
        string command
    ) {
        using TestDirectory directory = new();
        directory.Write(header, source);
        Assert.Equal(0, Run(directory, header, "--language", language).ExitCode);

        using StringWriter output = new();
        using StringWriter error = new();
        int exitCode = CliInvocation.Run([command, config], directory.Path, output, error);

        Assert.True(exitCode == 0, output.ToString() + error.ToString());
        Assert.NotEmpty(Directory.GetFiles(directory.Resolve("Generated"), "*.cs", SearchOption.AllDirectories));
        using JsonDocument document = directory.ReadJson(config);
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
            Assert.True(property.Name == "$schema" || char.IsLower(property.Name[0]), property.Name);
    }

    [Fact]
    public void Run_CHeader_WritesPortableRepositoryRelativePaths()
    {
        using TestDirectory directory = new();
        directory.Write("include/native.h", "int native_add(int left, int right);\n");

        CommandResult result = Run(directory, "include/native.h");

        Assert.Equal(0, result.ExitCode);
        using JsonDocument config = directory.ReadJson("bindgen.json");
        JsonElement root = config.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("include/native.h", root.GetProperty("source").GetProperty("headers")[0].GetString());
        Assert.Equal("include", root.GetProperty("source").GetProperty("includeDirectories")[0].GetString());
        Assert.DoesNotContain(directory.Path, File.ReadAllText(directory.Resolve("bindgen.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void Run_CppHeader_WithConfigOption_ResolvesPathsFromConfigDirectory()
    {
        using TestDirectory directory = new();
        directory.Write("include/library.hpp", "namespace Demo { class Widget {}; }\n");

        CommandResult result = Run(directory, "include/library.hpp", "--config", "bindings/bridge.json");

        Assert.Equal(0, result.ExitCode);
        using JsonDocument config = directory.ReadJson("bindings/bridge.json");
        JsonElement root = config.RootElement;
        Assert.Equal("../include/library.hpp", root.GetProperty("source").GetProperty("headers")[0].GetString());
        Assert.Equal("../include", root.GetProperty("source").GetProperty("includeDirectories")[0].GetString());
        Assert.Equal("cpp", root.GetProperty("source").GetProperty("language").GetString());
    }

    [Fact]
    public void Run_ConfigPath_UsesNativeHeaderBesideConfig()
    {
        using TestDirectory directory = new();

        CommandResult result = Run(directory, "bindings/bindgen.json");

        Assert.Equal(0, result.ExitCode);
        using JsonDocument config = directory.ReadJson("bindings/bindgen.json");
        JsonElement root = config.RootElement;
        Assert.Equal("native.h", root.GetProperty("source").GetProperty("headers")[0].GetString());
        Assert.Empty(root.GetProperty("source").GetProperty("includeDirectories").EnumerateArray());
    }

    [Theory]
    [InlineData("c", "bindgen.json")]
    [InlineData("cpp", "bindgen.json")]
    public void Run_LanguageOption_OverridesHeaderHeuristics(string language, string expectedConfig)
    {
        using TestDirectory directory = new();
        directory.Write("api.h", "class NativeApi {};\n");

        CommandResult result = Run(directory, "api.h", "--language", language);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(directory.Resolve(expectedConfig)));
    }

    [Fact]
    public void Run_ExistingConfig_FailsWithoutOverwriting()
    {
        using TestDirectory directory = new();
        directory.Write("native.h", "void native_tick(void);\n");
        directory.Write("bindgen.json", "{ \"sentinel\": true }\n");

        CommandResult result = Run(directory, "native.h");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("already exists", result.Error, StringComparison.Ordinal);
        Assert.Equal("{ \"sentinel\": true }\n", File.ReadAllText(directory.Resolve("bindgen.json")));
    }

    [Fact]
    public void Run_InvalidLanguage_ReturnsUsageError()
    {
        using TestDirectory directory = new();
        directory.Write("native.h", "void native_tick(void);\n");

        CommandResult result = Run(directory, "native.h", "--language", "objective-c");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("auto, c, cpp", result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(directory.Resolve("bindgen.json")));
    }

    private static CommandResult Run(TestDirectory directory, params string[] args)
    {
        using StringWriter output = new();
        using StringWriter error = new();
        int exitCode = CliInvocation.Run(["init", .. args], directory.Path, output, error);
        return new(exitCode, output.ToString(), error.ToString());
    }

    private sealed record CommandResult(int ExitCode, string Output, string Error);

    private sealed class TestDirectory : IDisposable
    {
        public TestDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bgcs-tool-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string Resolve(string relativePath) => System.IO.Path.Combine(Path, relativePath);

        public void Write(string relativePath, string contents)
        {
            string path = Resolve(relativePath);
            string? parent = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);
            File.WriteAllText(path, contents);
        }

        public JsonDocument ReadJson(string relativePath) => JsonDocument.Parse(File.ReadAllText(Resolve(relativePath)));

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, true);
        }
    }
}
