using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BGCS.Core.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Converters;
using Xunit;

namespace BGCS.Configuration.Tests;

public abstract class ConfigurationEntryTestBase
{
    protected static readonly string EntryTestsRoot = Path.Combine(AppContext.BaseDirectory, "entryTests");

    protected static GeneratedOutput Generate(
        string configFileName = "config.json",
        [CallerFilePath] string callerFilePath = "")
    {
        string? entryFolder = Path.GetDirectoryName(callerFilePath);
        if (string.IsNullOrEmpty(entryFolder))
            throw new DirectoryNotFoundException("Unable to resolve entry test folder from caller path.");

        string configPath = Path.Combine(entryFolder, configFileName);
        if (!File.Exists(configPath))
            throw new FileNotFoundException($"Config file not found: {configPath}", configPath);

        string temp = Path.Combine(Path.GetTempPath(), "bgcs-config-entry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        CopyDirectory(entryFolder, temp);

        string tempConfigPath = Path.Combine(temp, configFileName);
        string configJson = File.ReadAllText(tempConfigPath);
        JObject configObj = JObject.Parse(configJson);
        string headerFileName = configObj.Value<string>("HeaderFile") ?? "header.h";
        string headerPath = Path.Combine(temp, headerFileName);
        if (!File.Exists(headerPath))
            throw new FileNotFoundException($"Header file not found: {headerPath}", headerPath);

        string outputPath = Path.Combine(temp, "out");
        string previousCwd = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = temp;
            CsCodeGeneratorConfig config = CsCodeGeneratorConfig.Load(tempConfigPath, new ConfigComposer());
            CsCodeGenerator generator = new(config);

            bool success = generator.Generate(headerPath, outputPath);
            string diagnostics = string.Join(
                Environment.NewLine,
                generator.Messages.Select(x => $"[{x.Severtiy}] {x.Message}"));

            string bindingsPath = Path.Combine(outputPath, "Bindings.cs");
            string bindings = File.Exists(bindingsPath) ? File.ReadAllText(bindingsPath) : string.Empty;

            bool hasErrors = generator.Messages.Any(x => x.Severtiy is LogSeverity.Error or LogSeverity.Critical);
            return new GeneratedOutput(temp, success, hasErrors, diagnostics, bindings, config);
        }
        finally
        {
            Environment.CurrentDirectory = previousCwd;
        }
    }

    protected static GeneratedOutput GenerateFromEntryFolder(string entryFolder, string configFileName = "config.json")
    {
        string configPath = Path.Combine(entryFolder, configFileName);
        if (!File.Exists(configPath))
            throw new FileNotFoundException($"Config file not found: {configPath}", configPath);

        string temp = Path.Combine(Path.GetTempPath(), "bgcs-config-entry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        CopyDirectory(entryFolder, temp);

        string tempConfigPath = Path.Combine(temp, configFileName);
        string configJson = File.ReadAllText(tempConfigPath);
        JObject configObj = JObject.Parse(configJson);
        string headerFileName = configObj.Value<string>("HeaderFile") ?? "header.h";
        string headerPath = Path.Combine(temp, headerFileName);
        if (!File.Exists(headerPath))
            throw new FileNotFoundException($"Header file not found: {headerPath}", headerPath);

        string outputPath = Path.Combine(temp, "out");
        string previousCwd = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = temp;
            CsCodeGeneratorConfig config = CsCodeGeneratorConfig.Load(tempConfigPath, new ConfigComposer());
            CsCodeGenerator generator = new(config);

            bool success = generator.Generate(headerPath, outputPath);
            string diagnostics = string.Join(
                Environment.NewLine,
                generator.Messages.Select(x => $"[{x.Severtiy}] {x.Message}"));

            string bindingsPath = Path.Combine(outputPath, "Bindings.cs");
            string bindings = File.Exists(bindingsPath) ? File.ReadAllText(bindingsPath) : string.Empty;

            bool hasErrors = generator.Messages.Any(x => x.Severtiy is LogSeverity.Error or LogSeverity.Critical);
            return new GeneratedOutput(temp, success, hasErrors, diagnostics, bindings, config);
        }
        finally
        {
            Environment.CurrentDirectory = previousCwd;
        }
    }

    private static void CopyDirectory(string sourceFolder, string targetFolder)
    {
        foreach (string sourceFile in Directory.GetFiles(sourceFolder, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceFolder, sourceFile);
            string targetPath = Path.Combine(targetFolder, relativePath);
            string? targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            File.Copy(sourceFile, targetPath, overwrite: true);
        }
    }

    protected static void AssertGenerationSucceeded(GeneratedOutput output)
    {
        Assert.True(output.Success, output.Diagnostics);
        Assert.False(output.HasErrors, output.Diagnostics);
    }

    protected static void PrintBindings(GeneratedOutput output)
    {
        Console.WriteLine("==== Bindings.cs ====");
        Console.WriteLine(output.Bindings);
    }

    protected static void AssertExpected(
        GeneratedOutput output,
        string expectedFileName = "expected.json",
        [CallerFilePath] string callerFilePath = "")
    {
        string? entryFolder = Path.GetDirectoryName(callerFilePath);
        if (string.IsNullOrEmpty(entryFolder))
            throw new DirectoryNotFoundException("Unable to resolve entry test folder from caller path.");

        string expectedPath = Path.Combine(entryFolder, expectedFileName);
        if (!File.Exists(expectedPath))
            throw new FileNotFoundException($"Expected file not found: {expectedPath}", expectedPath);

        JObject expectedObj = JObject.Parse(File.ReadAllText(expectedPath));
        string propertyName = expectedObj.Value<string>("Property")
                              ?? throw new InvalidOperationException($"Missing Property in {expectedPath}");
        JToken expectedValue = expectedObj["Expected"]
                               ?? throw new InvalidOperationException($"Missing Expected in {expectedPath}");

        PropertyInfo? property = output.Config.GetType().GetProperty(propertyName);
        Assert.True(property is not null, $"Property '{propertyName}' not found on CsCodeGeneratorConfig.");

        object? actualValue = property!.GetValue(output.Config);
        JsonSerializer serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            Converters = [new StringEnumConverter()]
        });
        JToken actualToken = actualValue is null ? JValue.CreateNull() : JToken.FromObject(actualValue, serializer);

        Assert.True(
            JToken.DeepEquals(expectedValue, actualToken),
            $"Expected property '{propertyName}' to be '{expectedValue}', but was '{actualToken}'.");

        AssertBindingsExpected(output, callerFilePath: callerFilePath);
    }

    protected static void AssertExpectedForEntryFolder(
        GeneratedOutput output,
        string entryFolder,
        string expectedFileName = "expected.json")
    {
        string expectedPath = Path.Combine(entryFolder, expectedFileName);
        if (!File.Exists(expectedPath))
            throw new FileNotFoundException($"Expected file not found: {expectedPath}", expectedPath);

        JObject expectedObj = JObject.Parse(File.ReadAllText(expectedPath));
        string propertyName = expectedObj.Value<string>("Property")
                              ?? throw new InvalidOperationException($"Missing Property in {expectedPath}");
        JToken expectedValue = expectedObj["Expected"]
                               ?? throw new InvalidOperationException($"Missing Expected in {expectedPath}");

        PropertyInfo? property = output.Config.GetType().GetProperty(propertyName);
        Assert.True(property is not null, $"Property '{propertyName}' not found on CsCodeGeneratorConfig.");

        object? actualValue = property!.GetValue(output.Config);
        JsonSerializer serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            Converters = [new StringEnumConverter()]
        });
        JToken actualToken = actualValue is null ? JValue.CreateNull() : JToken.FromObject(actualValue, serializer);

        Assert.True(
            JToken.DeepEquals(expectedValue, actualToken),
            $"Expected property '{propertyName}' to be '{expectedValue}', but was '{actualToken}'.");

        AssertBindingsExpectedForEntryFolder(output, entryFolder);
    }

    protected static void AssertBindingsExpected(
        GeneratedOutput output,
        string expectedBindingsFileName = "expected.bindings.json",
        [CallerFilePath] string callerFilePath = "")
    {
        string? entryFolder = Path.GetDirectoryName(callerFilePath);
        if (string.IsNullOrEmpty(entryFolder))
            throw new DirectoryNotFoundException("Unable to resolve entry test folder from caller path.");

        string expectedPath = Path.Combine(entryFolder, expectedBindingsFileName);
        if (!File.Exists(expectedPath))
            return;

        JObject expectedObj = JObject.Parse(File.ReadAllText(expectedPath));
        foreach (JToken token in expectedObj["Contains"] as JArray ?? [])
        {
            string expectedText = token.Value<string>() ?? string.Empty;
            Assert.Contains(expectedText, output.Bindings);
        }

        foreach (JToken token in expectedObj["NotContains"] as JArray ?? [])
        {
            string expectedText = token.Value<string>() ?? string.Empty;
            Assert.DoesNotContain(expectedText, output.Bindings);
        }
    }

    protected static void AssertBindingsExpectedForEntryFolder(
        GeneratedOutput output,
        string entryFolder,
        string expectedBindingsFileName = "expected.bindings.json")
    {
        string expectedPath = Path.Combine(entryFolder, expectedBindingsFileName);
        if (!File.Exists(expectedPath))
            return;

        JObject expectedObj = JObject.Parse(File.ReadAllText(expectedPath));
        foreach (JToken token in expectedObj["Contains"] as JArray ?? [])
        {
            string expectedText = token.Value<string>() ?? string.Empty;
            Assert.Contains(expectedText, output.Bindings);
        }

        foreach (JToken token in expectedObj["NotContains"] as JArray ?? [])
        {
            string expectedText = token.Value<string>() ?? string.Empty;
            Assert.DoesNotContain(expectedText, output.Bindings);
        }
    }

    protected readonly record struct GeneratedOutput(
        string TempDirectory,
        bool Success,
        bool HasErrors,
        string Diagnostics,
        string Bindings,
        CsCodeGeneratorConfig Config) : IDisposable
    {
        public void Dispose()
        {
            if (Directory.Exists(TempDirectory))
            {
                Directory.Delete(TempDirectory, true);
            }
        }
    }
}
