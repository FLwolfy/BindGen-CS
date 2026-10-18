using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace BGCS.Tool.Commands;

internal static partial class InitCommand
{
    private const string C_DEFAULTCCONFIGNAME = "bindgen.json";
    private const string C_DEFAULTCPPCONFIGNAME = "bindgen.json";
    internal static int Run(
        string[] args,
        string workingDirectory,
        TextWriter output,
        TextWriter error
    ) {
        try
        {
            InitOptions options = Parse(args);
            string inputArgument = options.inputPath ?? global::BGCS.Tool.Commands.InitCommand.C_DEFAULTCCONFIGNAME;
            string inputPath = Path.GetFullPath(inputArgument, workingDirectory);
            string extension = Path.GetExtension(inputPath).ToLowerInvariant();
            bool isHeader = extension is ".h" or ".hh" or ".hpp" or ".hxx";
            if (isHeader && !File.Exists(inputPath))
                return Fail(error, $"Header does not exist: {inputPath}");
            bool isCpp = ResolveCppMode(options.language, extension, isHeader ? File.ReadAllText(inputPath) : null);
            string configPath = ResolveConfigPath(options, workingDirectory, inputPath, isHeader, isCpp);
            if (File.Exists(configPath))
                return Fail(error, $"Configuration already exists: {configPath}");
            string configDirectory = Path.GetDirectoryName(configPath) ?? throw new InvalidOperationException($"Cannot determine configuration directory for '{configPath}'.");
            Directory.CreateDirectory(configDirectory);
            string nativeInputPath = isHeader ? inputPath : Path.Combine(configDirectory, "native.h");
            string nativeInput = ToPortableConfigPath(configDirectory, nativeInputPath);
            string? includeFolder = Path.GetDirectoryName(nativeInput)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(includeFolder))
                includeFolder = ".";
            object config = CreateConfig(nativeInput, includeFolder, isCpp);
            JsonSerializerOptions serialization = new()
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            JsonObject document = (JsonObject)JsonSerializer.SerializeToNode(config, serialization)!;
            document["$schema"] = "./bindgen.schema.json";
            string schemaPath = Path.Combine(configDirectory, "bindgen.schema.json");
            string schema = new BGCS.Configuration.SchemaCatalog().CreateBindingSchema().ToJsonString(serialization) + Environment.NewLine;
            if (File.Exists(schemaPath) && File.ReadAllText(schemaPath) != schema)
                return Fail(error, $"An existing schema differs from this BGCS version: {schemaPath}. Export the current schema to a separately reviewed location first.");
            File.WriteAllText(schemaPath, schema);
            File.WriteAllText(configPath, document.ToJsonString(serialization) + Environment.NewLine);
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

    private static object CreateConfig(
        string nativeInput,
        string includeFolder,
        bool cpp
    ) => new
    {
        SchemaVersion = 1,
        Library = new { Id = "native", NativeName = cpp ? "native_bridge" : "native" },
        Source = new { Language = cpp ? "cpp" : "c", LanguageStandard = cpp ? "c++23" : "c17", Headers = new[] { nativeInput }, IncludeDirectories = includeFolder == "." ? Array.Empty<string>() : new[] { includeFolder }, IncludeTransitivelyReferencedHeaders = true },
        Targets = new[] { new { Id = "host" } },
        Api = new { Namespace = "Native.Bindings", Name = "NativeApi", Style = "instance", ImportMode = "functionTable", Framework = "net10.0" },
        Runtime = new { Mode = "package" },
        Output = new { Root = "Generated", Layout = "split" },
        Cache = new { Enabled = true }
    };
    private static string ResolveConfigPath(
        InitOptions options,
        string workingDirectory,
        string inputPath,
        bool isHeader,
        bool isCpp
    ) {
        if (!string.IsNullOrWhiteSpace(options.configPath))
        {
            if (!isHeader)
                throw new ArgumentException("--config can only be used when the input is a header.");
            if (!string.Equals(Path.GetExtension(options.configPath), ".json", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("--config must name a .json file.");
            return Path.GetFullPath(options.configPath, workingDirectory);
        }

        return isHeader ? Path.Combine(workingDirectory, isCpp ? global::BGCS.Tool.Commands.InitCommand.C_DEFAULTCPPCONFIGNAME : global::BGCS.Tool.Commands.InitCommand.C_DEFAULTCCONFIGNAME) : inputPath;
    }

    private static bool ResolveCppMode(
        InitLanguage language,
        string extension,
        string? headerText
    ) {
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

    private static string ToPortableConfigPath(
        string configDirectory,
        string path
    ) {
        string relativePath = Path.GetRelativePath(configDirectory, path);
        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException($"Cannot create a portable relative path from '{configDirectory}' to '{path}'. " + "Place the configuration and header on the same filesystem root.");
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

    private static string ReadValue(
        string[] args,
        ref int index,
        string option
    ) {
        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
            throw new ArgumentException($"Option '{option}' requires a value.");
        return args[index];
    }

    private static int Fail(
        TextWriter error,
        string message
    ) {
        error.WriteLine($"error: {message}");
        error.WriteLine("Run 'bindgen-cs --help' for usage.");
        return 2;
    }

    [GeneratedRegex("\\bnamespace\\s+[A-Za-z_]|\\btemplate\\s*<|\\bclass\\s+[A-Za-z_]|\\bextern\\s+\"C\\+\\+\"")]
    private static partial Regex CppSyntaxRegex();
    private sealed record InitOptions(
        string? inputPath,
        string? configPath,
        InitLanguage language
    );
    private enum InitLanguage
    {
        Auto,
        C,
        Cpp
    }
}
