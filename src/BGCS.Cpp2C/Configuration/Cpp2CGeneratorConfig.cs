using System;
using System.Collections.Generic;
using BGCS.Core.IO;
using BGCS.Core.Targeting;

namespace BGCS.Cpp2C.Configuration
{
    using System.ComponentModel;
    using BGCS.Core.Extensibility;
    using BGCS.Core.Logging;
    using BGCS.Cpp2C.Lowering;
    using BGCS.CppAst.Targeting;
    using BGCS.Intermediate;
    using Newtonsoft.Json;

    /// <summary>
    /// Configuration model for the C++ to C adapter generator.
    /// </summary>
    public partial class Cpp2CGeneratorConfig
    {
        /// <summary>Initializes the configuration and registers all built-in type lowerings through the public SPI.</summary>
        public Cpp2CGeneratorConfig()
        {
            foreach (ICppTypeLowering lowering in BuiltInCppTypeLowerings.all)
                this.lowerings.Register(lowering);
        }

        /// <summary>
        /// Runtime lowering registry. Extension instances are excluded from JSON; plugins register them before generation.
        /// </summary>
        [JsonIgnore, System.Text.Json.Serialization.JsonIgnore]
        public CppLoweringRegistry lowerings { get; } = new();
        /// <summary>Declarative type lowering recipes applied before parsing and emission.</summary>
        public List<CppTypeLoweringRecipe> typeLowerings { get; set; } = [];
        /// <summary>Declarative callable lowering recipes applied before parsing and emission.</summary>
        public List<CppCallableLoweringRecipe> callableLowerings { get; set; } = [];
        /// <summary>Explicit user-owned C ABI shim headers and sources copied into generated bridge output.</summary>
        public List<CppNativeShim> nativeShims { get; set; } = [];

        /// <summary>Controls whether only verified, user-asserted, or explicitly unsafe lowering extensions may run.</summary>
        [DefaultValue(CppLoweringSafetyPolicy.VerifiedOnly)]
        public CppLoweringSafetyPolicy loweringSafetyPolicy { get; set; } = CppLoweringSafetyPolicy.VerifiedOnly;

        /// <summary>Enables content-addressed restoration for unchanged configuration-driven bridge generation.</summary>
        [DefaultValue(true)]
        public bool enableIncrementalCache { get; set; } = true;

        /// <summary>Cache directory relative to the configuration file.</summary>
        [DefaultValue(".bindgen-cache")]
        public string cacheDirectory { get; set; } = ".bindgen-cache";
        /// <summary>Explicit third-party plugin assemblies, resolved relative to the configuration file.</summary>
        public List<string> pluginAssemblies { get; set; } = [];

        /// <summary>Runtime services registered by explicitly loaded plugins.</summary>
        [JsonIgnore, System.Text.Json.Serialization.JsonIgnore]
        public BindingPluginRegistry plugins { get; } = new();


        /// <summary>
        /// Optional base configuration source merged into this instance before generation.
        /// </summary>
        [DefaultValue(null)]
        public BaseConfig? baseConfig { get; set; }
        /// <summary>
        /// Root C++ headers parsed by configuration-driven generation.
        /// </summary>
        public List<string> entryFiles { get; set; } = [];
        /// <summary>
        /// Optional declaration-header whitelist. Empty uses entry headers.
        /// </summary>
        public List<string> allowedHeaders { get; set; } = [];

        /// <summary>
        /// Output directory resolved relative to the configuration file.
        /// </summary>
        [DefaultValue("GeneratedBridge")]
        public string outputPath { get; set; } = "GeneratedBridge";

        /// <summary>
        /// C++ language standard used when no explicit <c>-std=</c> compiler argument is supplied.
        /// </summary>
        [DefaultValue("c++23")]
        public string languageStandard { get; set; } = "c++23";

        /// <summary>
        /// Emits a deterministic, target-specific native bridge build manifest.
        /// </summary>
        [DefaultValue(true)]
        public bool generateBuildManifest { get; set; } = true;

        /// <summary>
        /// File name of the generated native bridge build manifest.
        /// </summary>
        [DefaultValue("bridge.manifest.json")]
        public string buildManifestFileName { get; set; } = "bridge.manifest.json";

        /// <summary>Gets or sets whether the CLI also generates C# bindings from the emitted C bridge header.</summary>
        [DefaultValue(false)]
        public bool generateCSharpBindings { get; set; }
        /// <summary>Gets or sets the namespace used by optional generated C# bridge bindings.</summary>
        public string cSharpNamespace { get; set; } = "NativeBindings";
        /// <summary>Gets or sets the API class name used by optional generated C# bridge bindings.</summary>
        public string cSharpApiName { get; set; } = "NativeBridge";
        /// <summary>Gets or sets the native bridge library name used by optional generated C# bindings.</summary>
        public string nativeLibraryName { get; set; } = "NativeBridge";
        /// <summary>Gets or sets the optional C# output path relative to the bridge configuration.</summary>
        public string cSharpOutputPath { get; set; } = "GeneratedBindings";

        /// <summary>Safety policy for optional C# bindings emitted from the generated C bridge.</summary>
        [DefaultValue(StrictSafetySeverity.SuppressFriendly)]
        public StrictSafetySeverity cSharpStrictSafetySeverity { get; set; } = StrictSafetySeverity.SuppressFriendly;
        /// <summary>
        /// The log level of the generator. (Default <see cref = "LogSeverity.Warning"/>)
        /// </summary>
        public LogSeverity logLevel { get; set; } = LogSeverity.Warning;
        /// <summary>
        /// The log level of the Clang Compiler. (Default <see cref = "LogSeverity.Error"/>)
        /// </summary>
        public LogSeverity cppLogLevel { get; set; } = LogSeverity.Error;

        /// <summary>
        /// Includes declarations originating from compiler system headers in the AST.
        /// </summary>
        [DefaultValue(false)]
        public bool parseSystemIncludes { get; set; }

        /// <summary>
        /// Parses preprocessor macros while building the C++ bridge AST.
        /// </summary>
        [DefaultValue(false)]
        public bool parseMacros { get; set; }

        /// <summary>
        /// Parses native documentation comments into the bridge metadata.
        /// </summary>
        [DefaultValue(false)]
        public bool parseComments { get; set; }
        /// <summary>
        /// List of the include folders. (Default: Empty)
        /// </summary>
        public List<string> includeFolders { get; set; } = new();
        /// <summary>
        /// List of the system include folders. (Default: Empty)
        /// </summary>
        public List<string> systemIncludeFolders { get; set; } = new();
        /// <summary>
        /// List of macros passed to CppAst. (Default: Empty)
        /// </summary>
        public List<string> defines { get; set; } = new();
        /// <summary>
        /// List of the additional arguments passed directly to the C++ Clang compiler. (Default: Empty)
        /// </summary>
        public List<string> additionalArguments { get; set; } = new();
        /// <summary>
        /// Library search directories used by native bridge build providers.
        /// </summary>
        public List<string> librarySearchFolders { get; set; } = [];
        /// <summary>
        /// Native libraries or library files linked by native bridge build providers.
        /// </summary>
        public List<string> linkLibraries { get; set; } = [];
        /// <summary>
        /// Additional arguments passed to the native linker by build providers.
        /// </summary>
        public List<string> linkerArguments { get; set; } = [];
        /// <summary>
        /// Fully qualified C++ class template specializations explicitly instantiated for bridge generation.
        /// </summary>
        public List<string> templateInstantiations { get; set; } = [];
        /// <summary>
        /// Complete explicit function-template specialization declarations, without the leading <c>template</c> keyword or semicolon.
        /// </summary>
        public List<string> functionTemplateInstantiations { get; set; } = [];
        /// <summary>
        /// C++ string type names lowered to borrowed UTF-8 C strings by the bridge.
        /// </summary>
        public List<string> utf8StringTypes { get; set; } = ["std::string", "std::basic_string<char>"];
        /// <summary>
        /// C++ unique-owner template names lowered to ownership-transferring opaque pointers.
        /// </summary>
        public List<string> uniquePtrTypes { get; set; } = ["std::unique_ptr"];
        /// <summary>
        /// C++ shared-owner template names lowered to lifetime-retained borrowed opaque pointers.
        /// </summary>
        public List<string> sharedPtrTypes { get; set; } = ["std::shared_ptr"];
        /// <summary>
        /// C++ contiguous-view template names lowered to pointer and element-count parameter pairs.
        /// </summary>
        public List<string> spanTypes { get; set; } = ["std::span"];
        /// <summary>
        /// C++ contiguous-container template names lowered to pointer/count pairs and borrowed return views.
        /// </summary>
        public List<string> vectorTypes { get; set; } = ["std::vector"];
        /// <summary>
        /// C++ optional-value template names lowered to explicit presence and output-value parameters.
        /// </summary>
        public List<string> optionalTypes { get; set; } = ["std::optional"];
        /// <summary>Fixed-size contiguous containers lowered to pointer/count pairs.</summary>
        public List<string> arrayTypes { get; set; } = ["std::array"];
        /// <summary>Ordered or unordered key/value containers lowered through owned opaque value holders.</summary>
        public List<string> mapTypes { get; set; } = ["std::map", "std::unordered_map"];
        /// <summary>Ordered or unordered unique-value containers lowered through owned opaque value holders.</summary>
        public List<string> setTypes { get; set; } = ["std::set", "std::unordered_set"];
        /// <summary>Discriminated unions lowered through typed opaque holders with index/access helpers.</summary>
        public List<string> variantTypes { get; set; } = ["std::variant"];
        /// <summary>Value/error results lowered through typed opaque holders with state/access helpers.</summary>
        public List<string> expectedTypes { get; set; } = ["std::expected"];
        /// <summary>Filesystem path values lowered to normalized UTF-8 at the bridge boundary.</summary>
        public List<string> pathTypes { get; set; } = ["std::filesystem::path"];
        /// <summary>Chrono durations lowered to signed nanoseconds.</summary>
        public List<string> chronoDurationTypes { get; set; } = ["std::chrono::duration"];
        /// <summary>Chrono time points lowered to signed nanoseconds since their clock epoch.</summary>
        public List<string> chronoTimePointTypes { get; set; } = ["std::chrono::time_point"];
        /// <summary>
        /// Fully qualified abstract C++ interfaces that receive managed callback proxy factories.
        /// </summary>
        public List<string> virtualCallbackInterfaces { get; set; } = [];

        /// <summary>
        /// Prefix prepended to generated C-facing symbol names.
        /// </summary>
        /// <remarks>
        /// Use this to avoid naming collisions when exposing multiple wrapped libraries in a single binary.
        /// </remarks>
        [DefaultValue("")]
        public string namePrefix { get; set; } = string.Empty;

        /// <summary>
        /// Gets the collision-safe prefix used by the generated exception channel.
        /// </summary>
        [JsonIgnore]
        public string errorSymbolPrefix => string.IsNullOrEmpty(this.namePrefix) ? "BGCS_" : this.namePrefix;

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
        /// Optional explicit Clang target triple.
        /// </summary>
        [DefaultValue(null)]
        public string? targetTriple { get; set; }

        /// <summary>
        /// Optional target SDK or sysroot used to parse C++ standard-library and platform headers.
        /// </summary>
        [DefaultValue(null)]
        public string? targetSysRoot { get; set; }

        /// <summary>
        /// Optional C++ compiler driver used for host system-header discovery and bridge verification.
        /// </summary>
        [DefaultValue(null)]
        public string? compilerPath { get; set; }

        /// <summary>
        /// Resolves target aliases into a validated concrete target.
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
    }
}
