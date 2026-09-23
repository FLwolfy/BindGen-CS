using System;
using System.IO;
using System.Text.Json;
using BGCS.Tool.Commands;
using Xunit;

namespace BGCS.Tool.Tests;

public sealed class InitCommandTests
{
    [Fact]
    public void Run_CHeader_WritesPortableRepositoryRelativePaths()
    {
        using TestDirectory directory = new();
        directory.Write("include/native.h", "int native_add(int left, int right);\n");

        CommandResult result = Run(directory, "include/native.h");

        Assert.Equal(0, result.ExitCode);
        using JsonDocument config = directory.ReadJson("bindgen.json");
        JsonElement root = config.RootElement;
        Assert.Equal(CsCodeGeneratorConfig.CurrentConfigVersion, root.GetProperty("ConfigVersion").GetInt32());
        Assert.Equal("IntermediateRepresentation", root.GetProperty("CSharpEmissionBackend").GetString());
        Assert.Equal("host-c,c-library", root.GetProperty("Preset").GetString());
        Assert.Equal("include/native.h", root.GetProperty("EntryFiles")[0].GetString());
        Assert.Equal("include", root.GetProperty("IncludeFolders")[0].GetString());
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
        Assert.Equal(BGCS.Cpp2C.Cpp2CGeneratorConfig.CurrentConfigVersion, root.GetProperty("ConfigVersion").GetInt32());
        Assert.Equal("../include/library.hpp", root.GetProperty("EntryFiles")[0].GetString());
        Assert.Equal("../include", root.GetProperty("IncludeFolders")[0].GetString());
        Assert.True(root.GetProperty("GenerateCSharpBindings").GetBoolean());
    }

    [Fact]
    public void Run_ConfigPath_UsesNativeHeaderBesideConfig()
    {
        using TestDirectory directory = new();

        CommandResult result = Run(directory, "bindings/bindgen.json");

        Assert.Equal(0, result.ExitCode);
        using JsonDocument config = directory.ReadJson("bindings/bindgen.json");
        JsonElement root = config.RootElement;
        Assert.Equal("native.h", root.GetProperty("EntryFiles")[0].GetString());
        Assert.Equal(".", root.GetProperty("IncludeFolders")[0].GetString());
    }

    [Theory]
    [InlineData("c", "bindgen.json")]
    [InlineData("cpp", "bridge.json")]
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
        int exitCode = InitCommand.Run(args, directory.Path, output, error);
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
