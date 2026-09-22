using System.Text.Json;
using System.Text.RegularExpressions;

namespace BGCS.Tool.Commands;

internal static partial class InitCommand
{
    private const string DefaultCConfigName = "bindgen.json";
    private const string DefaultCppConfigName = "bridge.json";

    internal static int Run(string[] args, string workingDirectory, TextWriter output, TextWriter error)
    {
        try
        {
            InitOptions options = Parse(args);
            string inputArgument = options.InputPath ?? DefaultCConfigName;
            string inputPath = Path.GetFullPath(inputArgument, workingDirectory);
            string extension = Path.GetExtension(inputPath).ToLowerInvariant();
            bool isHeader = extension is ".h" or ".hh" or ".hpp" or ".hxx";

            if (isHeader && !File.Exists(inputPath))
                return Fail(error, $"Header does not exist: {inputPath}");

            bool isCpp = ResolveCppMode(options.Language, extension, isHeader ? File.ReadAllText(inputPath) : null);
            string configPath = ResolveConfigPath(options, workingDirectory, inputPath, isHeader, isCpp);
            if (File.Exists(configPath))
                return Fail(error, $"Configuration already exists: {configPath}");

            string configDirectory = Path.GetDirectoryName(configPath)
                ?? throw new InvalidOperationException($"Cannot determine configuration directory for '{configPath}'.");
            Directory.CreateDirectory(configDirectory);

            string nativeInputPath = isHeader ? inputPath : Path.Combine(configDirectory, "native.h");
            string nativeInput = ToPortableConfigPath(configDirectory, nativeInputPath);
            string? includeFolder = Path.GetDirectoryName(nativeInput)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(includeFolder))
                includeFolder = ".";

            object config = isCpp
                ? CreateCppConfig(nativeInput, includeFolder)
                : CreateCConfig(nativeInput, includeFolder);

            File.WriteAllText(configPath, JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true
            }) + Environment.NewLine);

            output.WriteLine($"Created {configPath}");
            output.WriteLine($"Input: {nativeInput} ({(isCpp ? "C++ bridge" : "C ABI")})");
            return 0;
        }
        catch (ArgumentException exception)
        {
            return Fail(error, exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return Fail(error, exception.Message);
        }
    }

    private static object CreateCConfig(string nativeInput, string includeFolder) => new
    {
        ConfigVersion = CsCodeGeneratorConfig.CurrentConfigVersion,
        Preset = "host-c,c-library",
        Namespace = "Native.Bindings",
        ApiName = "NativeApi",
        LibName = "native",
        EntryFiles = new[] { nativeInput },
        AllowedHeaders = Array.Empty<string>(),
        IncludeTransitivelyReferencedHeaders = true,
        IncludeFolders = new[] { includeFolder },
        OutputPath = "Generated",
        ImportType = "DllImport",
        MergeGeneratedFilesToSingleFile = true
    };

    private static object CreateCppConfig(string nativeInput, string includeFolder) => new
    {
        ConfigVersion = BGCS.Cpp2C.Cpp2CGeneratorConfig.CurrentConfigVersion,
        EntryFiles = new[] { nativeInput },
        AllowedHeaders = new[] { nativeInput },
        IncludeFolders = new[] { includeFolder },
        OutputPath = "GeneratedBridge",
        LanguageStandard = "c++23",
        GenerateBuildManifest = true,
        GenerateCSharpBindings = true,
        CSharpNamespace = "Native.Bindings",
        CSharpApiName = "NativeApi",
        NativeLibraryName = "native",
        CSharpOutputPath = "Generated",
        ParseSystemIncludes = false,
        ParseComments = false
    };

    private static string ResolveConfigPath(
        InitOptions options,
        string workingDirectory,
        string inputPath,
        bool isHeader,
        bool isCpp)
    {
        if (!string.IsNullOrWhiteSpace(options.ConfigPath))
        {
            if (!isHeader)
                throw new ArgumentException("--config can only be used when the input is a header.");
            if (!string.Equals(Path.GetExtension(options.ConfigPath), ".json", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("--config must name a .json file.");
            return Path.GetFullPath(options.ConfigPath, workingDirectory);
        }

        return isHeader
            ? Path.Combine(workingDirectory, isCpp ? DefaultCppConfigName : DefaultCConfigName)
            : inputPath;
    }

    private static bool ResolveCppMode(InitLanguage language, string extension, string? headerText)
    {
        if (language == InitLanguage.C)
            return false;
        if (language == InitLanguage.Cpp)
            return true;
        if (extension is ".hh" or ".hpp" or ".hxx")
            return true;
        if (headerText == null)
            return false;

        return CppSyntaxRegex().IsMatch(headerText);
    }

    private static string ToPortableConfigPath(string configDirectory, string path)
    {
        string relativePath = Path.GetRelativePath(configDirectory, path);
        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException(
                $"Cannot create a portable relative path from '{configDirectory}' to '{path}'. " +
                "Place the configuration and header on the same filesystem root.");
        }
        return relativePath.Replace('\\', '/');
    }

    private static InitOptions Parse(string[] args)
    {
        string? inputPath = null;
        string? configPath = null;
        InitLanguage language = InitLanguage.Auto;

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            if (argument is "--language" or "-l")
            {
                string value = ReadValue(args, ref index, argument);
                language = value.ToLowerInvariant() switch
                {
                    "auto" => InitLanguage.Auto,
                    "c" => InitLanguage.C,
                    "cpp" or "c++" => InitLanguage.Cpp,
                    _ => throw new ArgumentException("--language must be one of: auto, c, cpp.")
                };
            }
            else if (argument is "--config" or "-c")
            {
                configPath = ReadValue(args, ref index, argument);
            }
            else if (argument.StartsWith("-", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unknown init option '{argument}'.");
            }
            else if (inputPath == null)
            {
                inputPath = argument;
            }
            else
            {
                throw new ArgumentException("init accepts at most one input path.");
            }
        }

        return new(inputPath, configPath, language);
    }

    private static string ReadValue(string[] args, ref int index, string option)
    {
        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
            throw new ArgumentException($"Option '{option}' requires a value.");
        return args[index];
    }

    private static int Fail(TextWriter error, string message)
    {
        error.WriteLine($"error: {message}");
        error.WriteLine("Run 'bindgen-cs --help' for usage.");
        return 2;
    }

    [GeneratedRegex("\\bnamespace\\s+[A-Za-z_]|\\btemplate\\s*<|\\bclass\\s+[A-Za-z_]|\\bextern\\s+\"C\\+\\+\"")]
    private static partial Regex CppSyntaxRegex();

    private sealed record InitOptions(string? InputPath, string? ConfigPath, InitLanguage Language);

    private enum InitLanguage
    {
        Auto,
        C,
        Cpp
    }
}
