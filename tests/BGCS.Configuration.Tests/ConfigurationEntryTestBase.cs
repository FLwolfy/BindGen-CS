using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BGCS.Core.Logging;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Converters;
using Xunit;

namespace BGCS.Configuration.Tests;

public abstract class ConfigurationEntryTestBase
{
    protected static readonly string EntryTestsRoot = Path.Combine(AppContext.BaseDirectory, "entryTests");

    protected static GeneratedOutput Generate(
        string configFileName,
        string[] headerFiles,
        string[] allowedHeaders,
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
        string[] resolvedHeaderFiles = ResolveHeaderFiles(temp, headerFiles);
        List<string> resolvedAllowedHeaders = ResolveAllowedHeaders(temp, allowedHeaders);

        string outputPath = Path.Combine(temp, "out");
        string previousCwd = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = temp;
            CsCodeGeneratorConfig config = CsCodeGeneratorConfig.Load(tempConfigPath, new ConfigComposer());
            CsCodeGenerator generator = new(config);

            bool success = generator.Generate(resolvedHeaderFiles.ToList(), outputPath, resolvedAllowedHeaders);
            string diagnostics = string.Join(
                Environment.NewLine,
                generator.Messages.Select(x => $"[{x.Severtiy}] {x.Message}"));

            string bindingsPath = Path.Combine(outputPath, "Bindings.cs");
            string bindings = File.Exists(bindingsPath) ? File.ReadAllText(bindingsPath) : string.Empty;

            bool hasErrors = generator.Messages.Any(x => x.Severtiy is LogSeverity.Error or LogSeverity.Critical);
            return new GeneratedOutput(temp, outputPath, success, hasErrors, diagnostics, bindings, config);
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

    private static string[] ResolveHeaderFiles(string tempFolder, string[] headerFiles)
    {
        if (headerFiles.Length == 0)
            throw new ArgumentException("headerFiles must contain at least one file.", nameof(headerFiles));

        string[] paths = headerFiles.Select(fileName => Path.Combine(tempFolder, fileName)).ToArray();
        foreach (string path in paths)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Header file not found: {path}", path);
        }

        return paths;
    }

    private static List<string> ResolveAllowedHeaders(string tempFolder, string[] allowedHeaders)
    {
        return
        [
            ..allowedHeaders.Select(header =>
                Path.IsPathRooted(header) ? header : Path.Combine(tempFolder, header))
        ];
    }

    protected static void AssertGenerationSucceeded(GeneratedOutput output)
    {
        Assert.True(output.Success, output.Diagnostics);
        Assert.False(output.HasErrors, output.Diagnostics);
        AssertBindingsCompiles(output);
    }

    protected static void AssertGenerationFailed(GeneratedOutput output)
    {
        Assert.True(!output.Success || output.HasErrors, output.Diagnostics);
    }

    protected static void PrintBindings(GeneratedOutput output)
    {
        Console.WriteLine("==== Diagnostics ====");
        Console.WriteLine(output.Diagnostics);
        Console.WriteLine("==== Bindings.cs ====");
        Console.WriteLine(output.Bindings);
    }

    protected static void AssertBindingsCompiles(GeneratedOutput output)
    {
        string[] generatedFiles = Directory.Exists(output.OutputDirectory)
            ? Directory.GetFiles(output.OutputDirectory, "*.cs", SearchOption.AllDirectories)
            : [];

        SyntaxTree[] trees = generatedFiles
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path))
            .ToArray();
        IEnumerable<MetadataReference> loadedReferences = AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(assembly =>
                !assembly.IsDynamic &&
                !string.IsNullOrWhiteSpace(assembly.Location) &&
                File.Exists(assembly.Location))
            .Select(assembly => MetadataReference.CreateFromFile(assembly.Location));

        IEnumerable<MetadataReference> localDllReferences = Directory
            .GetFiles(AppContext.BaseDirectory, "*.dll", SearchOption.TopDirectoryOnly)
            .Select(path => MetadataReference.CreateFromFile(path));

        MetadataReference[] references = loadedReferences
            .Concat(localDllReferences)
            .DistinctBy(reference => reference.Display)
            .ToArray();

        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: "BGCS.Configuration.Tests.GeneratedBindingsCheck",
            syntaxTrees: trees,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        Diagnostic[] errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        if (errors.Length == 0)
            return;

        string compileErrors = string.Join(Environment.NewLine, errors.Select(e => e.ToString()));
        Assert.True(false, $"Generated bindings failed to compile:{Environment.NewLine}{compileErrors}");
    }

    protected static void AssertExpected(
        GeneratedOutput output,
        string expectedFileName = "expected.json",
        string expectedBindingsFileName = "expected.bindings.json",
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

        AssertBindingsExpected(output, expectedBindingsFileName, callerFilePath);
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
        string OutputDirectory,
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
