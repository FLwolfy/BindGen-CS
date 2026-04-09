using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using BGCS.Core.Logging;
using BGCS.Cpp2C;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Xunit;

namespace BGCS.Cpp2C.Configuration.Tests;

public abstract class Cpp2CConfigurationEntryTestBase
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

        string temp = Path.Combine(Path.GetTempPath(), "bgcs-cpp2c-config-entry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        CopyDirectory(entryFolder, temp);

        string tempConfigPath = Path.Combine(temp, configFileName);
        string[] resolvedHeaderFiles = ResolveHeaderFiles(temp, headerFiles);
        List<string> resolvedAllowedHeaders = ResolveAllowedHeaders(temp, allowedHeaders);
        string outputPath = Path.Combine(temp, "out");

        string previousCwd = Environment.CurrentDirectory;
        Cpp2CGeneratorConfig config;
        Cpp2CCodeGenerator generator;
        try
        {
            Environment.CurrentDirectory = temp;
            config = Cpp2CGeneratorConfig.Load(tempConfigPath, new ConfigComposer());
            generator = new Cpp2CCodeGenerator(config);
            generator.Generate(resolvedHeaderFiles.ToList(), outputPath, resolvedAllowedHeaders);
        }
        finally
        {
            Environment.CurrentDirectory = previousCwd;
        }

        string diagnostics = string.Join(
            Environment.NewLine,
            generator.Messages.Select(x => $"[{x.Severtiy}] {x.Message}"));

        bool hasErrors = generator.Messages.Any(x => x.Severtiy is LogSeverity.Error or LogSeverity.Critical);
        string generatedText = ReadGeneratedText(outputPath);
        bool hasGeneratedFiles = Directory.Exists(outputPath) &&
                                 Directory.GetFiles(outputPath, "*.*", SearchOption.AllDirectories)
                                     .Any(file => file.EndsWith(".h", StringComparison.OrdinalIgnoreCase) ||
                                                  file.EndsWith(".hpp", StringComparison.OrdinalIgnoreCase) ||
                                                  file.EndsWith(".c", StringComparison.OrdinalIgnoreCase) ||
                                                  file.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase));
        bool success = !hasErrors && hasGeneratedFiles;

        return new GeneratedOutput(
            temp,
            outputPath,
            success,
            hasErrors,
            diagnostics,
            generatedText,
            config,
            resolvedHeaderFiles);
    }

    protected static void AssertGenerationSucceeded(GeneratedOutput output)
    {
        Assert.True(output.Success, output.Diagnostics);
        Assert.False(output.HasErrors, output.Diagnostics);
        AssertGeneratedSourcesCompile(output);
    }

    protected static void AssertGenerationFailed(GeneratedOutput output)
    {
        Assert.True(!output.Success || output.HasErrors, output.Diagnostics);
    }

    protected static void PrintGeneratedOutput(GeneratedOutput output)
    {
        Console.WriteLine("==== Diagnostics ====");
        Console.WriteLine(output.Diagnostics);
        Console.WriteLine("==== Generated Files ====");
        Console.WriteLine(output.GeneratedText);
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
        Assert.True(property is not null, $"Property '{propertyName}' not found on Cpp2CGeneratorConfig.");

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
            Assert.Contains(expectedText, output.GeneratedText);
        }

        foreach (JToken token in expectedObj["NotContains"] as JArray ?? [])
        {
            string expectedText = token.Value<string>() ?? string.Empty;
            Assert.DoesNotContain(expectedText, output.GeneratedText);
        }
    }

    private static string ReadGeneratedText(string outputPath)
    {
        if (!Directory.Exists(outputPath))
            return string.Empty;

        IEnumerable<string> files = Directory.GetFiles(outputPath, "*.*", SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".h", StringComparison.OrdinalIgnoreCase) ||
                           file.EndsWith(".hpp", StringComparison.OrdinalIgnoreCase) ||
                           file.EndsWith(".c", StringComparison.OrdinalIgnoreCase) ||
                           file.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file, StringComparer.Ordinal);

        return string.Join(
            Environment.NewLine + Environment.NewLine,
            files.Select(file =>
                $"// FILE: {Path.GetRelativePath(outputPath, file)}{Environment.NewLine}{File.ReadAllText(file)}"));
    }

    private static void CopyDirectory(string sourceFolder, string targetFolder)
    {
        foreach (string sourceFile in Directory.GetFiles(sourceFolder, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceFolder, sourceFile);
            string targetPath = Path.Combine(targetFolder, relativePath);
            string? targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDir))
                Directory.CreateDirectory(targetDir);

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

    private static void AssertGeneratedSourcesCompile(GeneratedOutput output)
    {
        string compiler = ResolveCompilerPath();

        string[] sources = Directory.Exists(output.OutputDirectory)
            ? Directory.GetFiles(output.OutputDirectory, "*.*", SearchOption.AllDirectories)
                .Where(file => file.EndsWith(".c", StringComparison.OrdinalIgnoreCase) ||
                               file.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase) ||
                               file.EndsWith(".cc", StringComparison.OrdinalIgnoreCase) ||
                               file.EndsWith(".cxx", StringComparison.OrdinalIgnoreCase))
                .OrderBy(file => file, StringComparer.Ordinal)
                .ToArray()
            : [];

        foreach (string source in sources)
        {
            string arguments = BuildCompileArguments(output, source);
            ProcessStartInfo startInfo = new()
            {
                FileName = compiler,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using Process process = Process.Start(startInfo)
                                    ?? throw new InvalidOperationException($"Failed to start compiler: {compiler}");
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            Assert.True(
                process.ExitCode == 0,
                $"Failed to compile generated source: {source}{Environment.NewLine}" +
                $"Compiler: {compiler}{Environment.NewLine}" +
                $"Arguments: {arguments}{Environment.NewLine}" +
                $"stdout:{Environment.NewLine}{stdout}{Environment.NewLine}" +
                $"stderr:{Environment.NewLine}{stderr}");
        }
    }

    private static string ResolveCompilerPath()
    {
        string? fromEnv = Environment.GetEnvironmentVariable("BGCS_CPP2C_CXX");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv;

        return "clang++";
    }

    private static string BuildCompileArguments(GeneratedOutput output, string source)
    {
        StringBuilder sb = new();
        sb.Append("-std=c++23 -fsyntax-only ");
        sb.Append(Quote(source));
        sb.Append(' ');

        string includeDir = Path.Combine(output.OutputDirectory, "include");
        if (Directory.Exists(includeDir))
        {
            sb.Append("-I");
            sb.Append(Quote(includeDir));
            sb.Append(' ');
        }

        HashSet<string> includeRoots = new(StringComparer.OrdinalIgnoreCase);
        includeRoots.UnionWith(
            output.HeaderFiles
                .Select(Path.GetDirectoryName)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!));
        includeRoots.UnionWith(ResolveConfigIncludeFolders(output));

        foreach (string root in includeRoots)
        {
            if (!Directory.Exists(root))
                continue;

            sb.Append("-I");
            sb.Append(Quote(root));
            sb.Append(' ');
        }

        foreach (string define in output.Config.Defines)
        {
            if (string.IsNullOrWhiteSpace(define))
                continue;

            string normalized = define.StartsWith("-D", StringComparison.Ordinal)
                ? define
                : $"-D{define}";
            sb.Append(normalized);
            sb.Append(' ');
        }

        foreach (string argument in output.Config.AdditionalArguments)
        {
            if (string.IsNullOrWhiteSpace(argument))
                continue;

            sb.Append(argument);
            sb.Append(' ');
        }

        foreach (string header in output.HeaderFiles)
        {
            sb.Append("-include ");
            sb.Append(Quote(header));
            sb.Append(' ');
        }

        return sb.ToString().TrimEnd();
    }

    private static IEnumerable<string> ResolveConfigIncludeFolders(GeneratedOutput output)
    {
        foreach (string folder in output.Config.IncludeFolders)
        {
            yield return Path.IsPathRooted(folder) ? folder : Path.Combine(output.TempDirectory, folder);
        }

        foreach (string folder in output.Config.SystemIncludeFolders)
        {
            yield return Path.IsPathRooted(folder) ? folder : Path.Combine(output.TempDirectory, folder);
        }
    }

    private static string Quote(string path)
    {
        return $"\"{path.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    protected readonly record struct GeneratedOutput(
        string TempDirectory,
        string OutputDirectory,
        bool Success,
        bool HasErrors,
        string Diagnostics,
        string GeneratedText,
        Cpp2CGeneratorConfig Config,
        string[] HeaderFiles) : IDisposable
    {
        public void Dispose()
        {
            if (Directory.Exists(TempDirectory))
                Directory.Delete(TempDirectory, true);
        }
    }
}
