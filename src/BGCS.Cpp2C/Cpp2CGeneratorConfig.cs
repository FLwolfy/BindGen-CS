namespace BGCS.Cpp2C
{
    using BGCS.Core.Logging;
    using System.ComponentModel;

    /// <summary>
    /// Configuration model for the C++ to C adapter generator.
    /// </summary>
    public partial class Cpp2CGeneratorConfig
    {
        /// <summary>
        /// Optional base configuration source merged into this instance before generation.
        /// </summary>
        [DefaultValue(null)]
        public BaseConfig? BaseConfig { get; set; }

        /// <summary>
        /// The log level of the generator. (Default <see cref="LogSeverity.Warning"/>)
        /// </summary>
        public LogSeverity LogLevel { get; set; } = LogSeverity.Warning;

        /// <summary>
        /// The log level of the Clang Compiler. (Default <see cref="LogSeverity.Error"/>)
        /// </summary>
        public LogSeverity CppLogLevel { get; set; } = LogSeverity.Error;

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
        /// Prefix prepended to generated C-facing symbol names.
        /// </summary>
        /// <remarks>
        /// Use this to avoid naming collisions when exposing multiple wrapped libraries in a single binary.
        /// </remarks>
        [DefaultValue("")]
        public string NamePrefix { get; set; } = string.Empty;
    }
}
