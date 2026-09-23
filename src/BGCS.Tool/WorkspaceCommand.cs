using System.Text.Json;
using BGCS.Intermediate;
using BGCS.Tool.Commands;

namespace BGCS.Tool;

internal static class WorkspaceCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static int Run(string[] args)
    {
        if (args.Length != 2 || args[0] is not ("validate" or "generate" or "diff"))
            throw new ArgumentException("workspace expects '<validate|generate|diff> <workspace.json>'.");

        string operation = args[0];
        string manifestPath = Path.GetFullPath(args[1]);
        BindingWorkspaceManifest manifest = LoadManifest(manifestPath);
        string manifestDirectory = Path.GetDirectoryName(manifestPath)!;
        List<string> configPaths = ResolveConfigs(manifest, manifestDirectory);

        return operation switch
        {
            "validate" => Validate(configPaths),
            "generate" => Generate(configPaths, manifest),
            "diff" => Diff(configPaths, manifest),
            _ => throw new InvalidOperationException($"Unsupported workspace operation '{operation}'.")
        };
    }

    private static BindingWorkspaceManifest LoadManifest(string manifestPath)
    {
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"Workspace manifest does not exist: {manifestPath}", manifestPath);

        BindingWorkspaceManifest? manifest = JsonSerializer.Deserialize<BindingWorkspaceManifest>(
            File.ReadAllText(manifestPath),
            JsonOptions);
        if (manifest?.Configs is not { Count: > 0 })
            throw new InvalidOperationException("Workspace manifest must contain at least one config path in 'Configs'.");
        return manifest;
    }

    private static List<string> ResolveConfigs(BindingWorkspaceManifest manifest, string manifestDirectory)
    {
        List<string> paths = [];
        HashSet<string> uniquePaths = new(StringComparer.OrdinalIgnoreCase);
        foreach (string configuredPath in manifest.Configs)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
                throw new InvalidOperationException("Workspace config paths cannot be empty.");
            string path = Path.GetFullPath(configuredPath, manifestDirectory);
            if (!File.Exists(path))
                throw new FileNotFoundException($"Workspace config does not exist: {path}", path);
            if (!uniquePaths.Add(path))
                throw new InvalidOperationException($"Workspace contains duplicate config path: {path}");
            paths.Add(path);
        }
        return paths;
    }

    private static int Validate(IReadOnlyList<string> configPaths)
    {
        foreach (string configPath in configPaths)
        {
            Console.WriteLine($"Validating {configPath}");
            CsCodeGenerator generator = CsCodeGenerator.Create(configPath);
            generator.LogToConsole();
            BindingGenerationResult result = generator.AnalyzeConfigured();
            if (!result.Success || result.Module == null)
            {
                GenerationDiagnosticWriter.WriteFailure(result, Console.Error);
                return 1;
            }
            if (result.Module.StructuredDiagnostics.Any(diagnostic =>
                    diagnostic.Severity == BindingDiagnosticSeverity.Error))
            {
                GenerationDiagnosticWriter.WriteFailure(result, Console.Error);
                return 1;
            }
        }
        Console.WriteLine($"Validated {configPaths.Count} binding configurations.");
        return 0;
    }

    private static int Generate(IReadOnlyList<string> configPaths, BindingWorkspaceManifest manifest)
    {
        // Validate every input before replacing any generated directory.
        if (Validate(configPaths) != 0)
            return 1;

        foreach (string configPath in configPaths)
        {
            Console.WriteLine($"Generating {configPath}");
            CsCodeGenerator generator = CsCodeGenerator.Create(configPath);
            generator.LogToConsole();
            if (!generator.GenerateConfigured(ResolveOutputPath(configPath, manifest)))
            {
                GenerationDiagnosticWriter.WriteFailure(generator.LastResult, Console.Error);
                return 1;
            }
        }
        Console.WriteLine($"Generated {configPaths.Count} binding projects.");
        return 0;
    }

    private static int Diff(IReadOnlyList<string> configPaths, BindingWorkspaceManifest manifest)
    {
        bool hasChanges = false;
        foreach (string configPath in configPaths)
        {
            CsCodeGeneratorConfig config = new BGCS.Configuration.ConfigLoader().Load(configPath);
            string expectedOutput = ResolveOutputPath(configPath, config, manifest);
            string temporaryOutput = Path.Combine(Path.GetTempPath(), "bindgen-cs-workspace-diff-" + Guid.NewGuid().ToString("N"));
            try
            {
                CsCodeGenerator generator = new(config);
                if (!generator.GenerateConfigured(temporaryOutput))
                {
                    GenerationDiagnosticWriter.WriteFailure(generator.LastResult, Console.Error);
                    return 2;
                }
                IReadOnlyDictionary<string, string> expected = ReadDirectory(temporaryOutput);
                IReadOnlyDictionary<string, string> actual = Directory.Exists(expectedOutput)
                    ? ReadDirectory(expectedOutput)
                    : new Dictionary<string, string>();
                List<string> changes = Compare(expected, actual);
                foreach (string change in changes)
                    Console.WriteLine($"{Path.GetFileName(configPath)}: {change}");
                hasChanges |= changes.Count != 0;
            }
            finally
            {
                if (Directory.Exists(temporaryOutput))
                    Directory.Delete(temporaryOutput, true);
            }
        }

        if (!hasChanges)
            Console.WriteLine($"All {configPaths.Count} generated binding projects are up to date.");
        return hasChanges ? 1 : 0;
    }

    private static string? ResolveOutputPath(string configPath, BindingWorkspaceManifest manifest)
    {
        if (!manifest.TargetOutputSubdirectories)
            return null;
        CsCodeGeneratorConfig config = new BGCS.Configuration.ConfigLoader().Load(configPath);
        return ResolveOutputPath(configPath, config, manifest);
    }

    private static string ResolveOutputPath(
        string configPath,
        CsCodeGeneratorConfig config,
        BindingWorkspaceManifest manifest)
    {
        string configDirectory = Path.GetDirectoryName(configPath)!;
        string output = Path.GetFullPath(config.OutputPath, configDirectory);
        return manifest.TargetOutputSubdirectories
            ? Path.Combine(output, config.ResolvedTarget.Identifier)
            : output;
    }

    private static IReadOnlyDictionary<string, string> ReadDirectory(string directory)
    {
        return Directory.GetFiles(directory, "*", SearchOption.AllDirectories).ToDictionary(
            path => Path.GetRelativePath(directory, path).Replace('\\', '/'),
            File.ReadAllText,
            StringComparer.OrdinalIgnoreCase);
    }

    private static List<string> Compare(
        IReadOnlyDictionary<string, string> expected,
        IReadOnlyDictionary<string, string> actual)
    {
        List<string> changes = [];
        foreach (string path in expected.Keys.Except(actual.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(path => path))
            changes.Add($"Added: {path}");
        foreach (string path in actual.Keys.Except(expected.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(path => path))
            changes.Add($"Removed: {path}");
        foreach (string path in expected.Keys.Intersect(actual.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(path => path))
        {
            if (!string.Equals(expected[path], actual[path], StringComparison.Ordinal))
                changes.Add($"Changed: {path}");
        }
        return changes;
    }

    private sealed class BindingWorkspaceManifest
    {
        public List<string> Configs { get; init; } = [];

        public bool TargetOutputSubdirectories { get; init; }
    }
}
