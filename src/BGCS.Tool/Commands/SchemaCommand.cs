using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using BGCS.Configuration;
using BGCS.Cpp2C.Configuration;

namespace BGCS.Tool.Commands;

internal static class SchemaCommand
{
    private static readonly IReadOnlyDictionary<string, string> CDescriptions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [nameof(CsCodeGeneratorConfig.preset)] = "Comma-separated named defaults applied before values from this configuration.",
        [nameof(CsCodeGeneratorConfig.@namespace)] = "Managed namespace for generated bindings.",
        [nameof(CsCodeGeneratorConfig.apiName)] = "Managed container name for generated native entry points.",
        [nameof(CsCodeGeneratorConfig.libName)] = "Native library name used by generated imports.",
        [nameof(CsCodeGeneratorConfig.entryFiles)] = "Header entry points, resolved relative to the configuration file.",
        [nameof(CsCodeGeneratorConfig.allowedHeaders)] = "Optional declaration-output whitelist; an empty list uses the configured transitive-header policy.",
        [nameof(CsCodeGeneratorConfig.includeFolders)] = "User include directories, resolved relative to the configuration file.",
        [nameof(CsCodeGeneratorConfig.systemIncludeFolders)] = "System include directories passed to the parser.",
        [nameof(CsCodeGeneratorConfig.outputPath)] = "Generated-output directory, resolved relative to the configuration file.",
        [nameof(CsCodeGeneratorConfig.targetId)] = "Open native target ID resolved by the configured target providers; host uses the generator process target.",
        [nameof(CsCodeGeneratorConfig.importType)] = "Interop import strategy used by generated functions.",
        [nameof(CsCodeGeneratorConfig.targetTriple)] = "Optional compiler target triple override.",
        [nameof(CsCodeGeneratorConfig.targetSysRoot)] = "Optional target SDK or sysroot path.",
        [nameof(CsCodeGeneratorConfig.marshallingMappings)] = "Function-level ownership, encoding, length, allocation, and cleanup contracts.",
        [nameof(CsCodeGeneratorConfig.externalTypeContracts)] = "Target-specific ABI evidence for project-supplied managed value-type carriers, including an explicit by-value safety policy.",
        [nameof(CsCodeGeneratorConfig.strictSafety)] = "Enables diagnostics for native semantics that cannot be proven safely.",
        [nameof(CsCodeGeneratorConfig.generateRuntimeSource)] = "Emits standalone Runtime.cs instead of requiring the BGCS.Runtime package."
    };
    private static readonly IReadOnlyDictionary<string, string> CppDescriptions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [nameof(Cpp2CGeneratorConfig.entryFiles)] = "C++ header entry points, resolved relative to the configuration file.",
        [nameof(Cpp2CGeneratorConfig.allowedHeaders)] = "Optional declaration-output whitelist.",
        [nameof(Cpp2CGeneratorConfig.includeFolders)] = "User include directories required to parse and compile the bridge.",
        [nameof(Cpp2CGeneratorConfig.outputPath)] = "Directory for generated C bridge headers and C++ implementation files.",
        [nameof(Cpp2CGeneratorConfig.languageStandard)] = "C++ language standard used unless AdditionalArguments contains an explicit -std= option.",
        [nameof(Cpp2CGeneratorConfig.generateBuildManifest)] = "Emits the machine-readable target-specific native bridge build contract.",
        [nameof(Cpp2CGeneratorConfig.buildManifestFileName)] = "JSON file name for the native bridge build contract.",
        [nameof(Cpp2CGeneratorConfig.librarySearchFolders)] = "Library search directories passed to native bridge build providers.",
        [nameof(Cpp2CGeneratorConfig.linkLibraries)] = "Native library names or files linked into the generated bridge.",
        [nameof(Cpp2CGeneratorConfig.linkerArguments)] = "Additional native linker arguments preserved in the build manifest.",
        [nameof(Cpp2CGeneratorConfig.templateInstantiations)] = "Fully qualified class-template specializations to instantiate explicitly.",
        [nameof(Cpp2CGeneratorConfig.functionTemplateInstantiations)] = "Complete function-template specialization declarations to expose.",
        [nameof(Cpp2CGeneratorConfig.typeLowerings)] = "Declarative, deterministic C++ type-to-C ABI lowering recipes.",
        [nameof(Cpp2CGeneratorConfig.callableLowerings)] = "Declarative callable selection, naming, and invocation-lowering recipes.",
        [nameof(Cpp2CGeneratorConfig.nativeShims)] = "Explicit user-owned C ABI shim headers and sources copied into bridge output.",
        [nameof(Cpp2CGeneratorConfig.loweringSafetyPolicy)] = "Maximum accepted trust level for declarative and plugin-supplied lowering code.",
        [nameof(Cpp2CGeneratorConfig.pluginAssemblies)] = "Typed lowering plugin assemblies loaded relative to this configuration.",
        [nameof(Cpp2CGeneratorConfig.generateCSharpBindings)] = "Generates matching C# bindings from the emitted C bridge header.",
        [nameof(Cpp2CGeneratorConfig.cSharpOutputPath)] = "Output directory for optional matching C# bindings.",
        [nameof(Cpp2CGeneratorConfig.cSharpStrictSafetySeverity)] = "Safety policy for optional C# bindings: suppress uncertain friendly APIs by default, retain them with Warning, or reject generation with Error.",
        [nameof(Cpp2CGeneratorConfig.nativeLibraryName)] = "Native library name used by optional generated C# imports.",
        [nameof(Cpp2CGeneratorConfig.targetTriple)] = "Optional compiler target triple override.",
        [nameof(Cpp2CGeneratorConfig.targetSysRoot)] = "Optional target SDK or sysroot path."
    };
    internal static int Run(
        string[] args,
        string workingDirectory,
        TextWriter output,
        TextWriter error
    ) {
        try
        {
            SchemaOptions options = Parse(args);
            bool cpp = string.Equals(options.kind, "cpp", StringComparison.Ordinal);
            Type configType = cpp ? typeof(Cpp2CGeneratorConfig) : typeof(CsCodeGeneratorConfig);
            string defaultFileName = cpp ? "bridge.schema.json" : "bindgen.schema.json";
            string outputPath = Path.GetFullPath(options.outputPath ?? defaultFileName, workingDirectory);
            IReadOnlyDictionary<string, string> descriptions = cpp ? CppDescriptions : CDescriptions;
            SortedDictionary<string, object?> properties = CreateObjectProperties(configType, descriptions, []);
            Dictionary<string, object?> document = new()
            {
                ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
                ["title"] = cpp ? "BindGen-CS C++ bridge configuration" : "BindGen-CS C binding configuration",
                ["description"] = cpp ? "Configuration contract for generating a C ABI bridge and optional C# bindings from C++." : "Configuration contract for generating C# bindings from a C ABI.",
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = cpp ? new[]
                {
                    nameof(Cpp2CGeneratorConfig.entryFiles)
                }

                : new[]
                {
                    nameof(CsCodeGeneratorConfig.@namespace),
                    nameof(CsCodeGeneratorConfig.apiName),
                    nameof(CsCodeGeneratorConfig.libName),
                    nameof(CsCodeGeneratorConfig.entryFiles)
                },
                ["additionalProperties"] = options.allowUnknownProperties
            };
            string? directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(outputPath, JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
            output.WriteLine($"Created {outputPath} ({(cpp ? "cpp" : "c")}, unknown properties {(options.allowUnknownProperties ? "allowed" : "rejected")})");
            return 0;
        }
        catch (ArgumentException exception)
        {
            error.WriteLine($"error: {exception.Message}");
            error.WriteLine("Run 'bindgen-cs --help' for usage.");
            return 2;
        }
    }

    private static SortedDictionary<string, object?> CreateObjectProperties(
        Type type,
        IReadOnlyDictionary<string, string> descriptions,
        HashSet<Type> stack
    ) {
        SortedDictionary<string, object?> properties = new(StringComparer.Ordinal);
        if (!stack.Add(type))
            return properties;
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(property => property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0).Where(property => !property.IsDefined(typeof(Newtonsoft.Json.JsonIgnoreAttribute), inherit: true)).Where(property => !property.IsDefined(typeof(System.Text.Json.Serialization.JsonIgnoreAttribute), inherit: true)).OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            Dictionary<string, object?> schema = CreatePropertySchema(property.PropertyType, descriptions, stack);
            if (descriptions.TryGetValue(property.Name, out string? description))
                schema["description"] = description;
            DefaultValueAttribute? defaultValue = property.GetCustomAttribute<DefaultValueAttribute>();
            if (defaultValue?.Value != null)
                schema["default"] = defaultValue.Value is Enum enumValue ? enumValue.ToString() : defaultValue.Value;
            properties[property.Name] = schema;
        }

        stack.Remove(type);
        return properties;
    }

    private static Dictionary<string, object?> CreatePropertySchema(
        Type type,
        IReadOnlyDictionary<string, string> descriptions,
        HashSet<Type> stack
    ) {
        Type? nullableType = Nullable.GetUnderlyingType(type);
        Type actualType = nullableType ?? type;
        object JsonType(string name) => nullableType == null ? name : new[]
        {
            name,
            "null"
        };
        if (actualType.IsEnum)
            return new()
            {
                ["type"] = JsonType("string"),
                ["enum"] = Enum.GetNames(actualType)
            };
        if (actualType == typeof(string) || actualType == typeof(char) || actualType == typeof(Guid) || actualType == typeof(Uri))
            return new()
            {
                ["type"] = JsonType("string")
            };
        if (actualType == typeof(bool))
            return new()
            {
                ["type"] = JsonType("boolean")
            };
        if (actualType == typeof(byte) || actualType == typeof(short) || actualType == typeof(int) || actualType == typeof(long) || actualType == typeof(sbyte) || actualType == typeof(ushort) || actualType == typeof(uint) || actualType == typeof(ulong))
            return new()
            {
                ["type"] = JsonType("integer")
            };
        if (actualType == typeof(float) || actualType == typeof(double) || actualType == typeof(decimal))
            return new()
            {
                ["type"] = JsonType("number")
            };
        Type? dictionaryInterface = FindGenericContract(actualType, typeof(IDictionary<,>));
        if (dictionaryInterface != null)
        {
            return new()
            {
                ["type"] = JsonType("object"),
                ["additionalProperties"] = CreatePropertySchema(dictionaryInterface.GetGenericArguments()[1], descriptions, stack)
            };
        }

        Type? enumerableInterface = actualType == typeof(string) ? null : FindGenericContract(actualType, typeof(IEnumerable<>));
        if (enumerableInterface != null || actualType.IsArray)
        {
            Type elementType = actualType.IsArray ? actualType.GetElementType() ?? typeof(object) : enumerableInterface!.GetGenericArguments()[0];
            return new()
            {
                ["type"] = JsonType("array"),
                ["items"] = CreatePropertySchema(elementType, descriptions, stack)
            };
        }

        if (actualType == typeof(object) || actualType.IsInterface || actualType.IsAbstract || stack.Contains(actualType))
            return new()
            {
                ["type"] = JsonType("object"),
                ["additionalProperties"] = true
            };
        return new()
        {
            ["type"] = JsonType("object"),
            ["properties"] = CreateObjectProperties(actualType, descriptions, stack),
            ["additionalProperties"] = false
        };
    }

    private static Type? FindGenericContract(
        Type type,
        Type genericDefinition
    ) {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == genericDefinition)
            return type;
        return type.GetInterfaces().FirstOrDefault(value => value.IsGenericType && value.GetGenericTypeDefinition() == genericDefinition);
    }

    private static SchemaOptions Parse(string[] args)
    {
        string kind = "c";
        string? outputPath = null;
        bool allowUnknownProperties = false;
        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            if (argument is "--kind" or "-k")
            {
                kind = ReadValue(args, ref index, argument).ToLowerInvariant();
                if (kind is not ("c" or "cpp"))
                    throw new ArgumentException("--kind must be one of: c, cpp.");
            }
            else if (argument == "--allow-unknown-properties")
            {
                allowUnknownProperties = true;
            }
            else if (argument.StartsWith("-", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unknown schema option '{argument}'.");
            }
            else if (outputPath == null)
            {
                outputPath = argument;
            }
            else
            {
                throw new ArgumentException("schema accepts at most one output path.");
            }
        }

        return new(kind, outputPath, allowUnknownProperties);
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

    private sealed record SchemaOptions(
        string kind,
        string? outputPath,
        bool allowUnknownProperties
    );
}
