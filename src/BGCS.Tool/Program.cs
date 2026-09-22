using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using BGCS.Cpp2C;
using BGCS.CppAst.Parsing;
using BGCS.CppAst.Targeting;
using BGCS.Emission;
using BGCS.Intermediate;

namespace BGCS.Tool;

internal static class Program
{
    private const string DefaultConfigPath = "bindgen.json";

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args.Any(IsHelp))
            {
                PrintHelp();
                return 0;
            }

            return args[0].ToLowerInvariant() switch
            {
                "init" => Initialize(args[1..]),
                "doctor" => Doctor(),
                "validate" => Validate(args[1..]),
                "inspect" => Inspect(args[1..]),
                "generate" => Generate(args[1..]),
                "build" => Build(args[1..]),
                "diff" => Diff(args[1..]),
                "workspace" => WorkspaceCommand.Run(args[1..]),
                "schema" => Schema(args[1..]),
                "bridge" => Bridge(args[1..]),
                "version" or "--version" => PrintVersion(),
                _ when args[0].EndsWith(".json", StringComparison.OrdinalIgnoreCase) => Generate(args),
                _ => Fail($"Unknown command '{args[0]}'.")
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            return 2;
        }
    }

    private static int Initialize(string[] args)
    {
        if (args.Length > 1)
        {
            return Fail("init accepts zero or one configuration path.");
        }

        string argument = args.Length == 0 ? DefaultConfigPath : args[0];
        string inputPath = Path.GetFullPath(argument);
        string extension = Path.GetExtension(inputPath).ToLowerInvariant();
        bool isHeader = extension is ".h" or ".hh" or ".hpp" or ".hxx";
        if (isHeader && !File.Exists(inputPath))
            return Fail($"Header does not exist: {inputPath}");
        bool isCpp = extension is ".hh" or ".hpp" or ".hxx" ||
            isHeader && (File.ReadAllText(inputPath).Contains("namespace ", StringComparison.Ordinal) ||
                         File.ReadAllText(inputPath).Contains("template<", StringComparison.Ordinal));
        string path = isHeader
            ? Path.Combine(Environment.CurrentDirectory, isCpp ? "bridge.json" : DefaultConfigPath)
            : inputPath;
        if (File.Exists(path))
            return Fail($"Configuration already exists: {path}");

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        string nativeInput = isHeader ? inputPath : Path.Combine(Path.GetDirectoryName(path)!, "native.h");
        object config = isCpp
            ? new
            {
                EntryFiles = new[] { inputPath },
                AllowedHeaders = new[] { inputPath },
                IncludeFolders = new[] { Path.GetDirectoryName(inputPath)! },
                OutputPath = "GeneratedBridge",
                GenerateCSharpBindings = true,
                CSharpNamespace = "Native.Bindings",
                CSharpApiName = "NativeApi",
                NativeLibraryName = "native",
                CSharpOutputPath = "Generated",
                ParseSystemIncludes = false,
                ParseComments = false
            }
            : new
            {
                Preset = "host-c",
                Namespace = "Native.Bindings",
                ApiName = "NativeApi",
                LibName = "native",
                EntryFiles = new[] { nativeInput },
                AllowedHeaders = Array.Empty<string>(),
                IncludeTransitivelyReferencedHeaders = true,
                IncludeFolders = new[] { Path.GetDirectoryName(nativeInput)! },
                OutputPath = "Generated",
                ImportType = "DllImport",
                MergeGeneratedFilesToSingleFile = true
            };
        File.WriteAllText(path, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Created {path} for {nativeInput}");
        return 0;
    }

    private static int Doctor()
    {
        CppTarget target = CppTarget.Resolve();
        string? clangPath = CppToolchainDiscovery.FindCompiler(CppParserKind.Cpp);
        bool clang = clangPath != null;
        IReadOnlyList<string> includes = clang
            ? CppToolchainDiscovery.DiscoverSystemIncludeFolders(CppParserKind.Cpp, clangPath)
            : [];
        Console.WriteLine($"OS: {Environment.OSVersion}");
        Console.WriteLine($".NET: {Environment.Version}");
        Console.WriteLine($"Host target: {target.Identifier} ({target.Triple})");
        Console.WriteLine($"C++ compiler: {(clang ? clangPath : "not found; set BGCS_CPP2C_CXX")}");
        Console.WriteLine($"System includes: {includes.Count}");
        if (target.Platform == CppTargetPlatform.MacOS)
            Console.WriteLine($"macOS SDK: {CppToolchainDiscovery.FindMacOsSdkRoot() ?? "not found"}");
        return clang && includes.Count > 0 ? 0 : 1;
    }

    private static int Validate(string[] args)
    {
        string configPath = ParseConfigPath(args);
        CsCodeGenerator generator = CsCodeGenerator.Create(configPath);
        generator.LogToConsole();
        BindingGenerationResult result = generator.AnalyzeConfigured();
        if (!result.Success)
            return 1;
        Console.WriteLine($"Validated {result.Module!.Types.Count} types and {result.Module.Functions.Count} functions.");
        if (result.Module.Diagnostics.Count > 0)
        {
            Console.WriteLine($"IR diagnostics: {result.Module.Diagnostics.Count}");
            return 1;
        }
        return 0;
    }

    private static int Inspect(string[] args)
    {
        bool json = args.Contains("--json", StringComparer.Ordinal);
        string configPath = ParseConfigPath(args.Where(argument => argument != "--json").ToArray());
        CsCodeGenerator generator = CsCodeGenerator.Create(configPath);
        BindingGenerationResult result = generator.AnalyzeConfigured();
        if (!result.Success || result.Module == null)
            return 1;
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(result.Module, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            Console.WriteLine($"Module: {result.Module.Name}");
            Console.WriteLine($"Target ABI: {result.Module.TargetAbi}");
            Console.WriteLine($"Types: {result.Module.Types.Count}");
            Console.WriteLine($"Functions: {result.Module.Functions.Count}");
            Console.WriteLine($"IR diagnostics: {result.Module.Diagnostics.Count}");
        }
        return result.Module.Diagnostics.Count == 0 ? 0 : 1;
    }

    private static int Bridge(string[] args)
    {
        (string configPath, string? outputPath) = ParseGenerateArguments(args);
        Cpp2CGeneratorConfig config = Cpp2CGeneratorConfig.Load(configPath);
        Cpp2CCodeGenerator generator = new(config);
        generator.GenerateConfigured(outputPath);
        if (generator.LastResult?.Success != true)
            return 1;
        Console.WriteLine($"Generated C++ bridge with {generator.LastResult.Module!.Types.Count} types and {generator.LastResult.Module.Functions.Count} functions.");
        if (config.GenerateCSharpBindings)
        {
            string bridgeHeader = generator.LastResult.OutputFiles.Single(path =>
                string.Equals(Path.GetFileName(path), "Classes.h", StringComparison.OrdinalIgnoreCase));
            string bridgeInclude = Path.GetDirectoryName(bridgeHeader)!;
            string baseDirectory = Path.GetDirectoryName(Path.GetFullPath(configPath))!;
            CsCodeGeneratorConfig csharpConfig = new()
            {
                Namespace = config.CSharpNamespace,
                ApiName = config.CSharpApiName,
                LibName = config.NativeLibraryName,
                TargetPlatform = config.TargetPlatform,
                TargetArchitecture = config.TargetArchitecture,
                TargetAbi = config.TargetAbi,
                TargetTriple = config.TargetTriple,
                TargetSysRoot = config.TargetSysRoot,
                CompilerPath = config.CompilerPath,
                ParserKind = BGCS.CppAst.Parsing.CppParserKind.C,
                AutoSquashTypedef = false,
                ParseMacros = false,
                ParseComments = false,
                ParseSystemIncludes = false,
                DelegatesAsVoidPointer = true,
                IncludeTransitivelyReferencedHeaders = true,
                ImportType = ImportType.DllImport,
                GenerateExtensions = false,
                OneFilePerType = false,
                MergeGeneratedFilesToSingleFile = true
            };
            csharpConfig.IncludeFolders.Add(bridgeInclude);
            new global::BGCS.ConfigComposer().Compose(ref csharpConfig);
            BGCS.Configuration.ConfigValidator.Validate(csharpConfig);
            CsCodeGenerator csharpGenerator = new(csharpConfig);
            if (!csharpGenerator.Generate(bridgeHeader, Path.GetFullPath(config.CSharpOutputPath, baseDirectory)))
                return 1;
            Console.WriteLine($"Generated C# bridge bindings in {Path.GetFullPath(config.CSharpOutputPath, baseDirectory)}.");
        }
        return 0;
    }

    private static int Schema(string[] args)
    {
        if (args.Length > 1)
            return Fail("schema accepts zero or one output path.");
        string outputPath = Path.GetFullPath(args.Length == 0 ? "bindgen.schema.json" : args[0]);
        SortedDictionary<string, object?> properties = new(StringComparer.Ordinal);
        foreach (PropertyInfo property in typeof(CsCodeGeneratorConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(property => property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0)
                     .OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            Dictionary<string, object?> schema = CreatePropertySchema(property.PropertyType);
            DefaultValueAttribute? defaultValue = property.GetCustomAttribute<DefaultValueAttribute>();
            if (defaultValue != null && defaultValue.Value != null)
                schema["default"] = defaultValue.Value;
            properties[property.Name] = schema;
        }
        Dictionary<string, object?> document = new()
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["title"] = "BindGen-CS configuration",
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new[] { "Namespace", "ApiName", "LibName", "EntryFiles" },
            ["additionalProperties"] = true
        };
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Created {outputPath}");
        return 0;
    }

    private static Dictionary<string, object?> CreatePropertySchema(Type type)
    {
        Type actualType = Nullable.GetUnderlyingType(type) ?? type;
        if (actualType.IsEnum)
            return new() { ["type"] = "string", ["enum"] = Enum.GetNames(actualType) };
        if (actualType == typeof(bool))
            return new() { ["type"] = "boolean" };
        if (actualType == typeof(byte) || actualType == typeof(short) || actualType == typeof(int) || actualType == typeof(long) ||
            actualType == typeof(ushort) || actualType == typeof(uint) || actualType == typeof(ulong))
            return new() { ["type"] = "integer" };
        if (actualType == typeof(float) || actualType == typeof(double) || actualType == typeof(decimal))
            return new() { ["type"] = "number" };
        Type? dictionaryInterface = actualType.GetInterfaces().FirstOrDefault(value =>
            value.IsGenericType && value.GetGenericTypeDefinition() == typeof(IDictionary<,>));
        if (dictionaryInterface != null)
            return new() { ["type"] = "object", ["additionalProperties"] = CreatePropertySchema(dictionaryInterface.GetGenericArguments()[1]) };
        if (actualType != typeof(string) && actualType.GetInterfaces().Any(value => value.IsGenericType && value.GetGenericTypeDefinition() == typeof(IEnumerable<>)))
        {
            Type? elementType = actualType.IsArray ? actualType.GetElementType() : actualType.GetGenericArguments().FirstOrDefault();
            return new() { ["type"] = "array", ["items"] = CreatePropertySchema(elementType ?? typeof(object)) };
        }
        return new() { ["type"] = actualType == typeof(string) ? "string" : "object" };
    }

    private static int Diff(string[] args)
    {
        (string configPath, string? outputPath) = ParseGenerateArguments(args);
        string fullConfigPath = Path.GetFullPath(configPath);
        CsCodeGeneratorConfig config = new BGCS.Configuration.ConfigLoader().Load(fullConfigPath);
        string baseDirectory = Path.GetDirectoryName(fullConfigPath)!;
        string expectedOutput = Path.GetFullPath(outputPath ?? config.OutputPath, baseDirectory);
        string temp = Path.Combine(Path.GetTempPath(), "bindgen-cs-diff-" + Guid.NewGuid().ToString("N"));
        try
        {
            CsCodeGenerator generator = new(config);
            if (!generator.GenerateConfigured(temp))
                return 2;
            Dictionary<string, string> expected = ReadDirectory(temp);
            Dictionary<string, string> actual = Directory.Exists(expectedOutput) ? ReadDirectory(expectedOutput) : [];
            List<string> changes = [];
            foreach (string path in expected.Keys.Except(actual.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(path => path))
                changes.Add($"Added: {path}");
            foreach (string path in actual.Keys.Except(expected.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(path => path))
                changes.Add($"Removed: {path}");
            foreach (string path in expected.Keys.Intersect(actual.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(path => path))
                if (!string.Equals(expected[path], actual[path], StringComparison.Ordinal))
                    changes.Add($"Changed: {path}");
            if (changes.Count == 0)
            {
                Console.WriteLine("Generated bindings are up to date.");
                return 0;
            }
            foreach (string change in changes)
                Console.WriteLine(change);
            return 1;
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    private static Dictionary<string, string> ReadDirectory(string directory)
    {
        return Directory.GetFiles(directory, "*", SearchOption.AllDirectories).ToDictionary(
            path => Path.GetRelativePath(directory, path).Replace('\\', '/'),
            File.ReadAllText,
            StringComparer.OrdinalIgnoreCase);
    }

    private static int Build(string[] args)
    {
        (string configPath, string? outputPath) = ParseGenerateArguments(args);
        CsCodeGenerator generator = CsCodeGenerator.Create(configPath);
        generator.LogToConsole();
        if (!generator.GenerateConfigured(outputPath) || generator.LastResult?.Module == null)
            return 1;

        string temp = Path.Combine(Path.GetTempPath(), "bindgen-cs-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            foreach (string sourceFile in generator.LastResult.OutputFiles.Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
                File.Copy(sourceFile, Path.Combine(temp, Path.GetFileName(sourceFile)), true);
            if (!File.Exists(Path.Combine(temp, "Runtime.cs")))
                new RuntimeEmitter().Emit(generator.LastResult.Module, new(temp, true, "Runtime.cs"));
            File.WriteAllText(Path.Combine(temp, "GeneratedBindings.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net9.0</TargetFramework>
                    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
                    <Nullable>enable</Nullable>
                    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                  </PropertyGroup>
                </Project>
                """);
            ProcessStartInfo startInfo = new(DotNetHostDiscovery.FindOrThrow())
            {
                WorkingDirectory = temp,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (string argument in new[] { "build", "GeneratedBindings.csproj", "--configuration", "Release", "--nologo" })
                startInfo.ArgumentList.Add(argument);
            using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start dotnet build.");
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            Task.WaitAll(stdout, stderr);
            Console.Write(stdout.Result);
            if (!string.IsNullOrWhiteSpace(stderr.Result))
                Console.Error.Write(stderr.Result);
            if (process.ExitCode != 0)
                return 1;
            Console.WriteLine("Generated bindings build validation passed.");
            return 0;
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    private static int Generate(string[] args)
    {
        (string configPath, string? outputPath) = ParseGenerateArguments(args);
        CsCodeGenerator generator = CsCodeGenerator.Create(configPath);
        generator.LogToConsole();
        bool success = generator.GenerateConfigured(outputPath);

        if (!success)
        {
            return 1;
        }
        Console.WriteLine($"Generated bindings from {Path.GetFullPath(configPath)}");
        return 0;
    }

    private static (string ConfigPath, string? OutputPath) ParseGenerateArguments(string[] args)
    {
        string configPath = DefaultConfigPath;
        string? outputPath = null;
        bool positionalConfigRead = false;
        for (int i = 0; i < args.Length; i++)
        {
            string argument = args[i];
            if (argument is "-c" or "--config")
            {
                configPath = ReadValue(args, ref i, argument);
            }
            else if (argument is "-o" or "--output")
            {
                outputPath = ReadValue(args, ref i, argument);
            }
            else if (IsHelp(argument))
            {
                PrintHelp();
                throw new OperationCanceledException("Generation was not started.");
            }
            else if (!argument.StartsWith('-') && !positionalConfigRead)
            {
                configPath = argument;
                positionalConfigRead = true;
            }
            else
            {
                throw new ArgumentException($"Unknown generate option '{argument}'.");
            }
        }
        return (configPath, outputPath);
    }

    private static string ParseConfigPath(string[] args)
    {
        if (args.Length == 0)
            return DefaultConfigPath;
        if (args.Length == 1 && !args[0].StartsWith('-'))
            return args[0];
        if (args.Length == 2 && args[0] is "-c" or "--config")
            return args[1];
        throw new ArgumentException("Expected an optional config path or '--config <path>'.");
    }

    private static string ReadValue(string[] args, ref int index, string option)
    {
        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
        {
            throw new ArgumentException($"Option '{option}' requires a value.");
        }
        return args[index];
    }

    private static bool IsHelp(string argument) => argument is "help" or "-h" or "--help";

    private static int PrintVersion()
    {
        Console.WriteLine(typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown");
        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"error: {message}");
        Console.Error.WriteLine("Run 'bindgen-cs --help' for usage.");
        return 2;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("BindGen-CS");
        Console.WriteLine("  bindgen-cs init [config.json|header.h|header.hpp]");
        Console.WriteLine("  bindgen-cs doctor");
        Console.WriteLine("  bindgen-cs validate [config.json]");
        Console.WriteLine("  bindgen-cs inspect [config.json] [--json]");
        Console.WriteLine("  bindgen-cs generate [config.json] [--output directory]");
        Console.WriteLine("  bindgen-cs build [config.json] [--output directory]");
        Console.WriteLine("  bindgen-cs diff [config.json] [--output directory]");
        Console.WriteLine("  bindgen-cs workspace <validate|generate|diff> <workspace.json>");
        Console.WriteLine("  bindgen-cs schema [output.json]");
        Console.WriteLine("  bindgen-cs bridge [config.json] [--output directory]");
        Console.WriteLine("  bindgen-cs <config.json> [--output directory]");
        Console.WriteLine("  bindgen-cs version");
    }
}
