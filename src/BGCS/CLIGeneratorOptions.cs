namespace BGCS
{
    using CommandLine;

    /// <summary>
    /// Defines the public class <c>CLIGeneratorOptions</c>.
    /// </summary>
    public class CLIGeneratorOptions
    {
        [Option('o', "output-dir", Required = false)]
        /// <summary>
        /// Gets or sets <c>OutputDirectory</c>.
        /// </summary>
        public string? OutputDirectory { get; set; } = null;

        [Option("targets", Default = "all", Required = false)]
        /// <summary>
        /// Gets or sets <c>Targets</c>.
        /// </summary>
        public string Targets { get; set; } = null!;
    }
}
