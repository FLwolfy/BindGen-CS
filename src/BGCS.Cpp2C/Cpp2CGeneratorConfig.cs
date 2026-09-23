namespace BGCS.Cpp2C
{
    using BGCS.Core.Logging;
    using BGCS.Core.Extensibility;
    using BGCS.CppAst.Parsing;
    using BGCS.CppAst.Targeting;
    using BGCS.Cpp2C.Lowering;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Converters;
    using System.ComponentModel;

    /// <summary>
    /// Configuration model for the C++ to C adapter generator.
    /// </summary>
    public partial class Cpp2CGeneratorConfig
    {
        /// <summary>Initializes the configuration and registers all built-in type lowerings through the public SPI.</summary>
        public Cpp2CGeneratorConfig()
        {
            foreach (ICppTypeLowering lowering in BuiltInCppTypeLowerings.All)
                Lowerings.Register(lowering);
        }

        /// <summary>
        /// Runtime lowering registry. Extension instances are excluded from JSON; plugins register them before generation.
        /// </summary>
        [JsonIgnore, System.Text.Json.Serialization.JsonIgnore]
        public CppLoweringRegistry Lowerings { get; } = new();

        /// <summary>Declarative type lowering recipes applied before parsing and emission.</summary>
        public List<CppTypeLoweringRecipe> TypeLowerings { get; set; } = [];

        /// <summary>Declarative callable lowering recipes applied before parsing and emission.</summary>
        public List<CppCallableLoweringRecipe> CallableLowerings { get; set; } = [];

        /// <summary>Explicit user-owned C ABI shim headers and sources copied into generated bridge output.</summary>
        public List<CppNativeShim> NativeShims { get; set; } = [];

        /// <summary>Controls whether only verified, user-asserted, or explicitly unsafe lowering extensions may run.</summary>
        [DefaultValue(CppLoweringSafetyPolicy.VerifiedOnly)]
        public CppLoweringSafetyPolicy LoweringSafetyPolicy { get; set; } = CppLoweringSafetyPolicy.VerifiedOnly;

        /// <summary>Enables content-addressed restoration for unchanged configuration-driven bridge generation.</summary>
        [DefaultValue(true)]
        public bool EnableIncrementalCache { get; set; } = true;

        /// <summary>Cache directory relative to the configuration file.</summary>
        [DefaultValue(".bindgen-cache")]
        public string CacheDirectory { get; set; } = ".bindgen-cache";

        /// <summary>Explicit third-party plugin assemblies, resolved relative to the configuration file.</summary>
        public List<string> PluginAssemblies { get; set; } = [];

        /// <summary>Runtime services registered by explicitly loaded plugins.</summary>
        [JsonIgnore, System.Text.Json.Serialization.JsonIgnore]
        public BindingPluginRegistry Plugins { get; } = new();

        /// <summary>
        /// Latest C++ bridge configuration contract version understood by this generator.
        /// </summary>
        public const int CurrentConfigVersion = 1;

        /// <summary>
        /// Version of the JSON configuration contract. Missing values deserialize as the current version.
        /// </summary>
        [DefaultValue(CurrentConfigVersion)]
        public int ConfigVersion { get; set; } = CurrentConfigVersion;

        /// <summary>
        /// Optional base configuration source merged into this instance before generation.
        /// </summary>
        [DefaultValue(null)]
        public BaseConfig? BaseConfig { get; set; }

        /// <summary>
        /// Root C++ headers parsed by configuration-driven generation.
        /// </summary>
        public List<string> EntryFiles { get; set; } = [];

        /// <summary>
        /// Optional declaration-header whitelist. Empty uses entry headers.
        /// </summary>
        public List<string> AllowedHeaders { get; set; } = [];

        /// <summary>
        /// Output directory resolved relative to the configuration file.
        /// </summary>
        [DefaultValue("GeneratedBridge")]
        public string OutputPath { get; set; } = "GeneratedBridge";

        /// <summary>
        /// C++ language standard used when no explicit <c>-std=</c> compiler argument is supplied.
        /// </summary>
        [DefaultValue("c++23")]
        public string LanguageStandard { get; set; } = "c++23";

        /// <summary>
        /// Emits a deterministic, target-specific native bridge build manifest.
        /// </summary>
        [DefaultValue(true)]
        public bool GenerateBuildManifest { get; set; } = true;

        /// <summary>
        /// File name of the generated native bridge build manifest.
        /// </summary>
        [DefaultValue("bridge.manifest.json")]
        public string BuildManifestFileName { get; set; } = "bridge.manifest.json";

        /// <summary>Gets or sets whether the CLI also generates C# bindings from the emitted C bridge header.</summary>
        [DefaultValue(false)]
        public bool GenerateCSharpBindings { get; set; }

        /// <summary>Gets or sets the namespace used by optional generated C# bridge bindings.</summary>
        public string CSharpNamespace { get; set; } = "NativeBindings";

        /// <summary>Gets or sets the API class name used by optional generated C# bridge bindings.</summary>
        public string CSharpApiName { get; set; } = "NativeBridge";

        /// <summary>Gets or sets the native bridge library name used by optional generated C# bindings.</summary>
        public string NativeLibraryName { get; set; } = "NativeBridge";

        /// <summary>Gets or sets the optional C# output path relative to the bridge configuration.</summary>
        public string CSharpOutputPath { get; set; } = "GeneratedBindings";

        /// <summary>
        /// The log level of the generator. (Default <see cref="LogSeverity.Warning"/>)
        /// </summary>
        public LogSeverity LogLevel { get; set; } = LogSeverity.Warning;

        /// <summary>
        /// The log level of the Clang Compiler. (Default <see cref="LogSeverity.Error"/>)
        /// </summary>
        public LogSeverity CppLogLevel { get; set; } = LogSeverity.Error;

        /// <summary>
        /// Includes declarations originating from compiler system headers in the AST.
        /// </summary>
        [DefaultValue(false)]
        public bool ParseSystemIncludes { get; set; }

        /// <summary>
        /// Parses preprocessor macros while building the C++ bridge AST.
        /// </summary>
        [DefaultValue(false)]
        public bool ParseMacros { get; set; }

        /// <summary>
        /// Parses native documentation comments into the bridge metadata.
        /// </summary>
        [DefaultValue(false)]
        public bool ParseComments { get; set; }

        /// <summary>
        /// List of the include folders. (Default: Empty)
        /// </summary>
        public List<string> IncludeFolders { get; set; } = new();

        /// <summary>
        /// List of the system include folders. (Default: Empty)
        /// </summary>
        public List<string> SystemIncludeFolders { get; set; } = new();

        /// <summary>
        /// List of macros passed to CppAst. (Default: Empty)
        /// </summary>
        public List<string> Defines { get; set; } = new();

        /// <summary>
        /// List of the additional arguments passed directly to the C++ Clang compiler. (Default: Empty)
        /// </summary>
        public List<string> AdditionalArguments { get; set; } = new();

        /// <summary>
        /// Library search directories used by native bridge build providers.
        /// </summary>
        public List<string> LibrarySearchFolders { get; set; } = [];

        /// <summary>
        /// Native libraries or library files linked by native bridge build providers.
        /// </summary>
        public List<string> LinkLibraries { get; set; } = [];

        /// <summary>
        /// Additional arguments passed to the native linker by build providers.
        /// </summary>
        public List<string> LinkerArguments { get; set; } = [];

        /// <summary>
        /// Fully qualified C++ class template specializations explicitly instantiated for bridge generation.
        /// </summary>
        public List<string> TemplateInstantiations { get; set; } = [];

        /// <summary>
        /// Complete explicit function-template specialization declarations, without the leading <c>template</c> keyword or semicolon.
        /// </summary>
        public List<string> FunctionTemplateInstantiations { get; set; } = [];

        /// <summary>
        /// C++ string type names lowered to borrowed UTF-8 C strings by the bridge.
        /// </summary>
        public List<string> Utf8StringTypes { get; set; } = ["std::string", "std::basic_string<char>"];

        /// <summary>
        /// C++ unique-owner template names lowered to ownership-transferring opaque pointers.
        /// </summary>
        public List<string> UniquePtrTypes { get; set; } = ["std::unique_ptr"];

        /// <summary>
        /// C++ shared-owner template names lowered to lifetime-retained borrowed opaque pointers.
        /// </summary>
        public List<string> SharedPtrTypes { get; set; } = ["std::shared_ptr"];

        /// <summary>
        /// C++ contiguous-view template names lowered to pointer and element-count parameter pairs.
        /// </summary>
        public List<string> SpanTypes { get; set; } = ["std::span"];

        /// <summary>
        /// C++ contiguous-container template names lowered to pointer/count pairs and borrowed return views.
        /// </summary>
        public List<string> VectorTypes { get; set; } = ["std::vector"];

        /// <summary>
        /// C++ optional-value template names lowered to explicit presence and output-value parameters.
        /// </summary>
        public List<string> OptionalTypes { get; set; } = ["std::optional"];

        /// <summary>Fixed-size contiguous containers lowered to pointer/count pairs.</summary>
        public List<string> ArrayTypes { get; set; } = ["std::array"];

        /// <summary>Ordered or unordered key/value containers lowered through owned opaque value holders.</summary>
        public List<string> MapTypes { get; set; } = ["std::map", "std::unordered_map"];

        /// <summary>Ordered or unordered unique-value containers lowered through owned opaque value holders.</summary>
        public List<string> SetTypes { get; set; } = ["std::set", "std::unordered_set"];

        /// <summary>Discriminated unions lowered through typed opaque holders with index/access helpers.</summary>
        public List<string> VariantTypes { get; set; } = ["std::variant"];

        /// <summary>Value/error results lowered through typed opaque holders with state/access helpers.</summary>
        public List<string> ExpectedTypes { get; set; } = ["std::expected"];

        /// <summary>Filesystem path values lowered to normalized UTF-8 at the bridge boundary.</summary>
        public List<string> PathTypes { get; set; } = ["std::filesystem::path"];

        /// <summary>Chrono durations lowered to signed nanoseconds.</summary>
        public List<string> ChronoDurationTypes { get; set; } = ["std::chrono::duration"];

        /// <summary>Chrono time points lowered to signed nanoseconds since their clock epoch.</summary>
        public List<string> ChronoTimePointTypes { get; set; } = ["std::chrono::time_point"];

        /// <summary>
        /// Fully qualified abstract C++ interfaces that receive managed callback proxy factories.
        /// </summary>
        public List<string> VirtualCallbackInterfaces { get; set; } = [];

        /// <summary>
        /// Prefix prepended to generated C-facing symbol names.
        /// </summary>
        /// <remarks>
        /// Use this to avoid naming collisions when exposing multiple wrapped libraries in a single binary.
        /// </remarks>
        [DefaultValue("")]
        public string NamePrefix { get; set; } = string.Empty;

        /// <summary>
        /// Gets the collision-safe prefix used by the generated exception channel.
        /// </summary>
        [JsonIgnore]
        public string ErrorSymbolPrefix => string.IsNullOrEmpty(NamePrefix) ? "BGCS_" : NamePrefix;

        /// <summary>
        /// Operating-system family targeted by the generated C bridge.
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        [DefaultValue(CppTargetPlatform.Host)]
        public CppTargetPlatform TargetPlatform { get; set; } = CppTargetPlatform.Host;

        /// <summary>
        /// Processor architecture targeted by the generated C bridge.
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        [DefaultValue(CppTargetArchitecture.Host)]
        public CppTargetArchitecture TargetArchitecture { get; set; } = CppTargetArchitecture.Host;

        /// <summary>
        /// Native ABI family. Default selects the conventional ABI for the target platform.
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        [DefaultValue(CppTargetAbi.Default)]
        public CppTargetAbi TargetAbi { get; set; } = CppTargetAbi.Default;

        /// <summary>
        /// Optional explicit Clang target triple.
        /// </summary>
        [DefaultValue(null)]
        public string? TargetTriple { get; set; }

        /// <summary>
        /// Optional target SDK or sysroot used to parse C++ standard-library and platform headers.
        /// </summary>
        [DefaultValue(null)]
        public string? TargetSysRoot { get; set; }

        /// <summary>
        /// Optional C++ compiler driver used for host system-header discovery and bridge verification.
        /// </summary>
        [DefaultValue(null)]
        public string? CompilerPath { get; set; }

        /// <summary>
        /// Resolves target aliases into a validated concrete target.
        /// </summary>
        [JsonIgnore, System.Text.Json.Serialization.JsonIgnore]
        public CppTarget ResolvedTarget => CppTarget.Resolve(TargetPlatform, TargetArchitecture, TargetAbi, TargetTriple);
    }
}
