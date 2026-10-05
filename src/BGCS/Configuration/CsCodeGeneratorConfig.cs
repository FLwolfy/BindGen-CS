using System;
using BGCS.Configuration.Mapping;
using BGCS.Configuration.Naming;
using BGCS.Core.IO;
using BGCS.Core.Targeting;

namespace BGCS.Configuration
{
    using System.Collections.Generic;
    using System.ComponentModel;
    using BGCS.Conversion;
    using BGCS.Core.Extensibility;
    using BGCS.Core.Logging;
    using BGCS.CppAst.Parsing;
    using BGCS.CppAst.Targeting;
    using BGCS.Intermediate;
    using BGCS.Metadata;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Converters;

    /// <summary>
    /// Complete configuration model for the BGCS C# binding generator.
    /// </summary>
    /// <remarks>
    /// This type controls parsing, filtering, naming, marshalling, file layout and emitted runtime integration.
    /// Most properties map directly to JSON configuration fields and are intended to be serialized/deserialized.
    /// </remarks>
    public partial class CsCodeGeneratorConfig : IGeneratorConfig
    {
        private readonly CppTypeConverter m_converter;
        /// <summary>
        /// Initializes a configuration instance with default collections and mapping tables.
        /// </summary>
        public CsCodeGeneratorConfig()
        {
            this.m_converter = new(this);
            this.functionTableEntries = [];
            this.knownConstantNames = [];
            this.knownEnumValueNames = [];
            this.knownEnumPrefixes = [];
            this.knownExtensionPrefixes = [];
            this.knownExtensionNames = [];
            this.knownDefaultValueNames = [];
            this.knownConstructors = [];
            this.knownMemberFunctions = [];
            this.functionPrefixes = [];
            this.ignoredParts = [];
            this.keywords = [];
            this.ignoredFunctions = [];
            this.ignoredTypes = [];
            this.ignoredEnums = [];
            this.ignoredTypedefs = [];
            this.ignoredDelegates = [];
            this.ignoredConstants = [];
            this.allowedFunctions = [];
            this.allowedTypes = [];
            this.allowedEnums = [];
            this.allowedTypedefs = [];
            this.allowedDelegates = [];
            this.allowedConstants = [];
            this.usings = [];
            this.entryFiles = [];
            this.allowedHeaders = [];
            this.includeFolders = [];
            this.systemIncludeFolders = [];
            this.defines = [];
            this.additionalArguments = [];
            this.customEnums = [];
            this.varyingTypes = [];
            this.constantMappings = [];
            this.enumMappings = [];
            this.functionMappings = [];
            this.variadicFunctionVariants = [];
            this.marshallingMappings = [];
            this.handleMappings = [];
            this.classMappings = [];
            this.delegateMappings = [];
            this.arrayMappings = [];
            this.nameMappings = [];
            this.typeMappings = [];
            this.externalTypeContracts = [];
            this.typedefToEnumMappings = [];
            this.functionAliasMappings = [];
            this.pluginAssemblies = [];
        }

        /// <summary>Enables content-addressed restoration for unchanged configuration-driven generation.</summary>
        [DefaultValue(true)]
        public bool enableIncrementalCache { get; set; } = true;

        /// <summary>Cache directory relative to the configuration file.</summary>
        [DefaultValue(".bindgen-cache")]
        public string cacheDirectory { get; set; } = ".bindgen-cache";
        /// <summary>Explicit third-party plugin assemblies, resolved relative to the configuration file.</summary>
        public List<string> pluginAssemblies { get; set; }

        /// <summary>Runtime services registered by explicitly loaded plugins.</summary>
        [JsonIgnore, System.Text.Json.Serialization.JsonIgnore]
        public BindingPluginRegistry plugins { get; } = new();

        [JsonIgnore, System.Text.Json.Serialization.JsonIgnore]
        internal HashSet<string> loadedPluginAssemblies { get; } = new(StringComparer.OrdinalIgnoreCase);


        /// <summary>
        /// Gets the type conversion service used to map C/C++ types to C# representations.
        /// </summary>
        [JsonIgnore]
        public CppTypeConverter typeConverter => this.m_converter;


        /// <summary>
        /// Optional base configuration source merged into this instance before generation.
        /// </summary>
        /// <remarks>
        /// This is typically used to reference shared settings from a central config URL/file.
        /// </remarks>
        [DefaultValue(null)]
        public BaseConfig? baseConfig { get; set; }

        /// <summary>
        /// Comma-separated named presets applied before validation.
        /// </summary>
        [DefaultValue("")]
        public string preset { get; set; } = string.Empty;

        /// <summary>
        /// The namespace of the generated wrapper. (Default <see cref = "string.Empty"/>)
        /// </summary>
        [DefaultValue("")]
        public string @namespace { get; set; } = string.Empty;

        /// <summary>
        /// The api name of the wrapper. (Used for exported functions and macros) (Default <see cref = "string.Empty"/>)
        /// </summary>
        [DefaultValue("")]
        public string apiName { get; set; } = string.Empty;

        /// <summary>
        /// The name of the .dll or .so or .dylib. (Default <see cref = "string.Empty"/>)
        /// </summary>
        [DefaultValue("")]
        public string libName { get; set; } = string.Empty;

        /// <summary>
        /// Header files analyzed by <see cref = "BGCS.Facade.CsCodeGenerator.GenerateConfigured"/>. Relative paths are resolved from the configuration directory.
        /// </summary>
        [DefaultValue(null)]
        public List<string> entryFiles { get; set; } = null!;

        /// <summary>
        /// Header files whose declarations may be emitted. An empty collection allows all entry files.
        /// </summary>
        [DefaultValue(null)]
        public List<string> allowedHeaders { get; set; } = null!;

        /// <summary>
        /// Includes declarations from headers transitively referenced under entry and include directories when no explicit AllowedHeaders are configured.
        /// </summary>
        [DefaultValue(false)]
        public bool includeTransitivelyReferencedHeaders { get; set; }

        /// <summary>
        /// Default output directory used by <see cref = "BGCS.Facade.CsCodeGenerator.GenerateConfigured"/>. (Default: <c>Generated</c>)
        /// </summary>
        [DefaultValue("Generated")]
        public string outputPath { get; set; } = "Generated";

        /// <summary>
        /// Gets or sets the open native target ID, or host to use the generator process target.
        /// </summary>
        [DefaultValue("host")]
        public string targetId { get; set; } = "host";

        /// <summary>
        /// Gets or sets the immutable target provider composition used by this generator.
        /// </summary>
        [JsonIgnore, System.Text.Json.Serialization.JsonIgnore]
        public ClangTargetResolver targetResolver { get; set; } = new();

        /// <summary>
        /// Optional explicit Clang target triple for specialized or versioned toolchains.
        /// </summary>
        [DefaultValue(null)]
        public string? targetTriple { get; set; }

        /// <summary>
        /// Optional target SDK or sysroot used when parsing platform and standard-library headers.
        /// </summary>
        [DefaultValue(null)]
        public string? targetSysRoot { get; set; }

        /// <summary>
        /// Optional C/C++ compiler driver used to discover host system include directories.
        /// </summary>
        [DefaultValue(null)]
        public string? compilerPath { get; set; }

        /// <summary>
        /// Resolves the configured target aliases into a validated concrete target.
        /// </summary>
        [JsonIgnore, System.Text.Json.Serialization.JsonIgnore]
        public NativeTargetDescriptor resolvedTarget
        {
            get
            {
                string directory = this.configDirectory ?? Environment.CurrentDirectory;
                var toolchain = new NativeToolchainDescriptor(
                    ConfigurationPath.Resolve(this.compilerPath, directory, allowCommandName: true),
                    ConfigurationPath.Resolve(this.targetSysRoot, directory, allowCommandName: false));
                return this.targetResolver.Resolve(new(new NativeTargetId(this.targetId), toolchain, this.targetTriple));
            }
        }

        /// <summary>
        /// The log level of the generator. (Default <see cref = "LogSeverity.Warning"/>)
        /// </summary>
        [DefaultValue(LogSeverity.Warning)]
        public LogSeverity logLevel { get; set; } = LogSeverity.Warning;

        /// <summary>
        /// The log level of the Clang Compiler. (Default <see cref = "LogSeverity.Error"/>)
        /// </summary>
        [DefaultValue(LogSeverity.Error)]
        public LogSeverity cppLogLevel { get; set; } = LogSeverity.Error;

        /// <summary>
        /// Includes declarations originating from compiler system headers in the AST. (Default: <see langword="false"/>)
        /// </summary>
        [DefaultValue(false)]
        public bool parseSystemIncludes { get; set; }

        /// <summary>
        /// Parses preprocessor macros for constant generation. Disable for macro-heavy headers when constants are not required.
        /// </summary>
        [DefaultValue(true)]
        public bool parseMacros { get; set; } = true;

        /// <summary>
        /// Parses native documentation comments for generated XML documentation.
        /// </summary>
        [DefaultValue(true)]
        public bool parseComments { get; set; } = true;

        /// <summary>
        /// Enables conservative diagnostics when ownership, allocator, buffer length, or callback lifetime cannot be proven.
        /// </summary>
        [DefaultValue(true)]
        public bool strictSafety { get; set; } = true;

        /// <summary>
        /// Gets or sets whether unresolved safety semantics warn or reject generation.
        /// </summary>
        [DefaultValue(StrictSafetySeverity.SuppressFriendly)]
        public StrictSafetySeverity strictSafetySeverity { get; set; } = StrictSafetySeverity.SuppressFriendly;

        /// <summary>
        /// Selects C, C++, or Objective-C language parsing. (Default: <see cref = "CppParserKind.Cpp"/>)
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        [DefaultValue(CppParserKind.Cpp)]
        public CppParserKind parserKind { get; set; } = CppParserKind.Cpp;

        /// <summary>
        /// This allows to use the (EXPERIMENTAL) options, otherwise they will be set back to false. (Default: <see langword="false"/>)
        /// </summary>
        [DefaultValue(false)]
        public bool enableExperimentalOptions { get; set; } = false;

        /// <summary>
        /// This option generates the sizes of the structs. (Default: <see langword="false"/>)
        /// </summary>
        [DefaultValue(false)]
        public bool generateSizeOfStructs { get; set; } = false;

        /// <summary>
        /// The generator will generate default constructors for all structs. (Default: <see langword="true"/>)
        /// </summary>
        [DefaultValue(true)]
        public bool generateConstructorsForStructs { get; set; } = true;

        /// <summary>
        /// Represents delegate values as opaque pointers instead of typed function pointers. The default is true.
        /// </summary>
        [DefaultValue(true)]
        public bool delegatesAsVoidPointer { get; set; } = true;

        /// <summary>
        /// Automatically wraps callback delegates with <c>NativeCallback&lt;T&gt;</c> holders before passing function pointers to native APIs.
        /// Useful for registration-style callbacks where native code stores the pointer beyond the call site. (Default: <see langword="false"/>)
        /// </summary>
        [DefaultValue(false)]
        public bool autoWrapCallbacks { get; set; } = false;

        /// <summary>
        /// This option makes the resulting wrapper more "safe" so you don't need unsafe blocks everywhere. (Default: <see langword="false"/>)
        /// </summary>
        [DefaultValue(false)]
        public bool wrapPointersAsHandle { get; set; } = false;

        /// <summary>
        /// This causes the code generator to generate summary xml comments if it's missing with the text "To be documented." (Default: <see langword="true"/>)
        /// </summary>
        [DefaultValue(true)]
        public bool generatePlaceholderComments { get; set; } = true;

        /// <summary>
        /// This causes the code generator to use <see cref = "System.Runtime.InteropServices.LibraryImportAttribute"/>.
        /// </summary>
        [JsonIgnore]
        public bool useLibraryImport => this.importType == ImportType.LibraryImport;

        /// <summary>
        /// This causes the code generator to use a FunctionTable.
        /// </summary>
        [JsonIgnore]
        public bool useFunctionTable => this.importType == ImportType.FunctionTable;

        /// <summary>
        /// Specifies the existing entries in the function table.
        /// </summary>
        [DefaultValue(null)]
        public List<CsFunctionTableEntry> functionTableEntries { get; set; } = null!;

        /// <summary>
        /// Indicates whether to use a custom context. (Default: false)
        /// </summary>
        [DefaultValue(false)]
        public bool useCustomContext { get; set; }

        /// <summary>
        /// The function name to get the library name. (Default: "GetLibraryName")
        /// </summary>
        [DefaultValue("GetLibraryName")]
        public string getLibraryNameFunctionName { get; set; } = "GetLibraryName";

        /// <summary>
        /// The function name to get the library extension. (Default: null)
        /// </summary>
        [DefaultValue(null)]
        public string? getLibraryExtensionFunctionName { get; set; } = null;

        /// <summary>
        /// Determines the import type. (Default: <see cref = "ImportType.LibraryImport"/>)
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        [DefaultValue(ImportType.FunctionTable)]
        public ImportType importType { get; set; } = ImportType.FunctionTable;

        /// <summary>
        /// Emits the default <c>LibName</c> constant used by DllImport and LibraryImport declarations.
        /// Disable this when a user-authored partial API class supplies a conditional or platform-specific constant.
        /// </summary>
        [DefaultValue(true)]
        public bool emitLibraryNameConstant { get; set; } = true;

        /// <summary>
        /// The generator will generate [NativeName] attributes.
        /// </summary>
        [DefaultValue(false)]
        public bool generateMetadata { get; set; } = false;

        /// <summary>
        /// Enables generation for constants (CPP: Macros) (Default: <see langword="true"/>)
        /// </summary>
        [DefaultValue(true)]
        public bool generateConstants { get; set; } = true;

        /// <summary>
        /// Enables generation for enums. (Default: <see langword="true"/>)
        /// </summary>
        [DefaultValue(true)]
        public bool generateEnums { get; set; } = true;

        /// <summary>
        /// Enables generation for extensions, this option is very useful if you have a handle type. (Default: <see langword="true"/>)
        /// </summary>
        [DefaultValue(true)]
        public bool generateExtensions { get; set; } = true;

        /// <summary>
        /// Enables generation for functions. This option generates the public API dllexport functions. (Default: <see langword="true"/>)
        /// </summary>
        [DefaultValue(true)]
        public bool generateFunctions { get; set; } = true;

        /// <summary>
        /// Enables generation for handles. (CPP: Typedefs) (Default: <see langword="true"/>)
        /// </summary>
        [DefaultValue(true)]
        public bool generateHandles { get; set; } = true;

        /// <summary>
        /// Enables generation for types. This includes normal C structs. (Default: <see langword="true"/>)
        /// </summary>
        [DefaultValue(true)]
        public bool generateTypes { get; set; } = true;

        /// <summary>
        /// Enables generation for delegates. (Default: <see langword="true"/>)
        /// </summary>
        [DefaultValue(true)]
        public bool generateDelegates { get; set; } = true;

        /// <summary>
        /// Generates each type into its own file when enabled. When disabled, type output may be consolidated.
        /// </summary>
        [DefaultValue(true)]
        public bool oneFilePerType { get; set; } = true;

        /// <summary>
        /// Nests generated enums, structs, handles, delegates, and opaque records inside the API class.
        /// This supports C libraries whose established managed surface uses a single container type.
        /// </summary>
        [DefaultValue(false)]
        public bool nestGeneratedTypesInApi { get; set; }

        /// <summary>
        /// Merge all generated .cs files into a single file in the output root directory. (Default: <see langword="false"/>)
        /// </summary>
        [DefaultValue(false)]
        public bool mergeGeneratedFilesToSingleFile { get; set; }


        /// <summary>
        /// File name used for merged C# bindings in the output root. (Default: <c>Bindings.cs</c>)
        /// </summary>
        [DefaultValue("Bindings.cs")]
        public string singleFileOutputName { get; set; } = "Bindings.cs";

        /// <summary>
        /// Runtime namespace used by generated bindings and optional standalone runtime source.
        /// If empty or whitespace, defaults to <c>BGCS.Runtime</c>.
        /// </summary>
        [DefaultValue("")]
        public string runtimeNamespace { get; set; } = string.Empty;

        /// <summary>
        /// Generate standalone runtime source file (`Runtime.cs`) into output root. (Default: <see langword="false"/>)
        /// </summary>
        [DefaultValue(false)]
        public bool generateRuntimeSource { get; set; } = false;

        /// <summary>
        /// Controls native bool representation in generated signatures.
        /// <see cref = "BoolType.Bool8"/> uses <c>BGCS.Runtime.Bool8</c>.
        /// <see cref = "BoolType.Bool32"/> uses <c>BGCS.Runtime.Bool32</c>.
        /// <see cref = "BoolType.Byte"/> and <see cref = "BoolType.Int32"/> use unmanaged numeric primitives.
        /// </summary>
        [DefaultValue(BoolType.Bool8)]
        public BoolType boolType { get; set; } = BoolType.Bool8;

        /// <summary>
        /// Allows to map names for constants. (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public Dictionary<string, string> knownConstantNames { get; set; } = null!;

        /// <summary>
        /// Allows to map names for enums. (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public Dictionary<string, string> knownEnumValueNames { get; set; } = null!;

        /// <summary>
        /// Allows to map names for enum prefixes. (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public Dictionary<string, string> knownEnumPrefixes { get; set; } = null!;

        /// <summary>
        /// Allows to map names for extension prefixes. (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public Dictionary<string, string> knownExtensionPrefixes { get; set; } = null!;

        /// <summary>
        /// Allows to map names for extension. (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public Dictionary<string, string> knownExtensionNames { get; set; } = null!;

        /// <summary>
        /// Allows to map names for default values. (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public Dictionary<string, string> knownDefaultValueNames { get; set; } = null!;

        /// <summary>
        /// Allows to define constructors functions for types. (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public Dictionary<string, List<string>> knownConstructors { get; set; } = null!;

        /// <summary>
        /// Allows to define member functions for types. (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public Dictionary<string, List<string>> knownMemberFunctions { get; set; } = null!;

        /// <summary>
        /// Exact native prefixes removed from function names before applying the configured naming convention.
        /// </summary>
        /// <remarks>
        /// When multiple prefixes match, the longest prefix wins. Explicit <see cref = "functionMappings"/>
        /// friendly names take precedence over this collection.
        /// </remarks>
        [DefaultValue(null)]
        public List<string> functionPrefixes { get; set; } = null!;

        /// <summary>
        /// Ignores parts like OpenAl in OpenALFunction -> Function. (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public HashSet<string> ignoredParts { get; set; } = null!;

        /// <summary>
        /// C# keywords that would cause issues with naming. (Default: all common C# keywords)
        /// </summary>
        [DefaultValue(null)]
        public HashSet<string> keywords { get; set; } = null!;

        /// <summary>
        /// All function names in this HashSet will be ignored in the generation process. (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public HashSet<string> ignoredFunctions { get; set; } = null!;
        /// <summary>
        /// All extension function names in this HashSet will be ignored in the generation process. (Default: Empty)
        /// </summary>
        public HashSet<string> ignoredExtensions { get; set; } = new();

        /// <summary>
        /// All types names in this HashSet will be ignored in the generation process. (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public HashSet<string> ignoredTypes { get; set; } = null!;

        /// <summary>
        /// All enums names in this HashSet will be ignored in the generation process. (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public HashSet<string> ignoredEnums { get; set; } = null!;

        /// <summary>
        /// All typedefs names in this HashSet will be ignored in the generation process. (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public HashSet<string> ignoredTypedefs { get; set; } = null!;

        /// <summary>
        /// All delegates names in this HashSet will be ignored in the generation process. (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public HashSet<string> ignoredDelegates { get; set; } = null!;

        /// <summary>
        /// All constants names in this HashSet will be ignored in the generation process. (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public HashSet<string> ignoredConstants { get; set; } = null!;

        /// <summary>
        /// Acts as a whitelist, if the list is empty no whitelisting is applied on functions. (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public HashSet<string> allowedFunctions { get; set; } = null!;
        /// <summary>
        /// Acts as a whitelist, if the list is empty no whitelisting is applied on extension functions. (Default: Empty)
        /// </summary>
        public HashSet<string> allowedExtensions { get; set; } = new();

        /// <summary>
        /// Acts as a whitelist, if the list is empty no whitelisting is applied on types. (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public HashSet<string> allowedTypes { get; set; } = null!;

        /// <summary>
        /// Acts as a whitelist, if the list is empty no whitelisting is applied on enums. (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public HashSet<string> allowedEnums { get; set; } = null!;

        /// <summary>
        /// Acts as a whitelist, if the list is empty no whitelisting is applied on typedefs. (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public HashSet<string> allowedTypedefs { get; set; } = null!;

        /// <summary>
        /// Acts as a whitelist, if the list is empty no whitelisting is applied on delegates. (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public HashSet<string> allowedDelegates { get; set; } = null!;

        /// <summary>
        /// Acts as a whitelist, if the list is empty no whitelisting is applied on constants. (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public HashSet<string> allowedConstants { get; set; } = null!;

        /// <summary>
        /// Allows to add or manage usings. (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public List<string> usings { get; set; } = null!;

        /// <summary>
        /// The naming convention for constants, set it to <see cref = "NamingConvention.Unknown"/> to keep the original name. (Default: <see cref = "NamingConvention.Unknown"/>)
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        [DefaultValue(NamingConvention.Unknown)]
        public NamingConvention constantNamingConvention { get; set; } = NamingConvention.Unknown;

        /// <summary>
        /// The naming convention for enums, set it to <see cref = "NamingConvention.Unknown"/> to keep the original name. (Default: <see cref = "NamingConvention.PascalCase"/>)
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        [DefaultValue(NamingConvention.PascalCase)]
        public NamingConvention enumNamingConvention { get; set; } = NamingConvention.PascalCase;

        /// <summary>
        /// The naming convention for enum items, set it to <see cref = "NamingConvention.Unknown"/> to keep the original name. (Default: <see cref = "NamingConvention.PascalCase"/>)
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        [DefaultValue(NamingConvention.PascalCase)]
        public NamingConvention enumItemNamingConvention { get; set; } = NamingConvention.PascalCase;

        /// <summary>
        /// The naming convention for extension functions, set it to <see cref = "NamingConvention.Unknown"/> to keep the original name. (Default: <see cref = "NamingConvention.PascalCase"/>)
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        [DefaultValue(NamingConvention.PascalCase)]
        public NamingConvention extensionNamingConvention { get; set; } = NamingConvention.PascalCase;

        /// <summary>
        /// The naming convention for functions, set it to <see cref = "NamingConvention.Unknown"/> to keep the original name. (Default: <see cref = "NamingConvention.PascalCase"/>)
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        [DefaultValue(NamingConvention.PascalCase)]
        public NamingConvention functionNamingConvention { get; set; } = NamingConvention.PascalCase;

        /// <summary>
        /// The naming convention for handles, set it to <see cref = "NamingConvention.Unknown"/> to keep the original name. (Default: <see cref = "NamingConvention.PascalCase"/>)
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        [DefaultValue(NamingConvention.PascalCase)]
        public NamingConvention handleNamingConvention { get; set; } = NamingConvention.PascalCase;

        /// <summary>
        /// The naming convention for classes and structs, set it to <see cref = "NamingConvention.Unknown"/> to keep the original name. (Default: <see cref = "NamingConvention.PascalCase"/>)
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        [DefaultValue(NamingConvention.PascalCase)]
        public NamingConvention typeNamingConvention { get; set; } = NamingConvention.PascalCase;

        /// <summary>
        /// The naming convention for delegates, set it to <see cref = "NamingConvention.Unknown"/> to keep the original name. (Default: <see cref = "NamingConvention.PascalCase"/>)
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        [DefaultValue(NamingConvention.PascalCase)]
        public NamingConvention delegateNamingConvention { get; set; } = NamingConvention.PascalCase;

        /// <summary>
        /// The naming convention for parameters, set it to <see cref = "NamingConvention.Unknown"/> to keep the original name. (Default: <see cref = "NamingConvention.CamelCase"/>)
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        [DefaultValue(NamingConvention.CamelCase)]
        public NamingConvention parameterNamingConvention { get; set; } = NamingConvention.CamelCase;

        /// <summary>
        /// The naming convention for members, set it to <see cref = "NamingConvention.Unknown"/> to keep the original name. (Default: <see cref = "NamingConvention.PascalCase"/>)
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        [DefaultValue(NamingConvention.PascalCase)]
        public NamingConvention memberNamingConvention { get; set; } = NamingConvention.PascalCase;

        /// <summary>
        /// Collapses typedef chains automatically before type mapping and emission.
        /// </summary>
        /// <remarks>
        /// Enabling this usually reduces alias noise in output but can hide intermediate typedef names.
        /// </remarks>
        [DefaultValue(true)]
        public bool autoSquashTypedef { get; set; } = true;

        /// <summary>
        /// List of the include folders. (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public List<string> includeFolders { get; set; } = null!;

        /// <summary>
        /// List of the system include folders. (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public List<string> systemIncludeFolders { get; set; } = null!;

        /// <summary>
        /// List of macros passed to CppAst. (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public List<string> defines { get; set; } = null!;

        /// <summary>
        /// List of the additional arguments passed directly to the C++ Clang compiler. (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public List<string> additionalArguments { get; set; } = null!;

        /// <summary>
        /// Additional enum definitions injected into generated output regardless of parsed source headers.
        /// </summary>
        [DefaultValue(null)]
        public List<CsEnumMetadata> customEnums { get; set; } = null!;

        /// <summary>
        /// A list of allowed types for generating additional overloads.
        /// </summary>
        [DefaultValue(null)]
        public HashSet<string> varyingTypes { get; set; } = null!;

        /// <summary>
        /// Generates additional overloads, <c>WARNING</c> this option can really generate many overloads. To filter which type is allowed use <see cref = "varyingTypes"/>
        /// </summary>
        [DefaultValue(false)]
        public bool generateAdditionalOverloads { get; set; }
    }
}
