namespace BGCS
{
    /// <summary>
    /// Represents compatibility command-line options accepted by generator builders.
    /// </summary>
    public class CLIGeneratorOptions
    {
        /// <summary>
        /// Gets or sets the optional output directory override.
        /// </summary>
        public string? OutputDirectory { get; set; }

        /// <summary>
        /// Gets or sets the selected generation targets.
        /// </summary>
        public string Targets { get; set; } = "all";

        internal static CLIGeneratorOptions Parse(IReadOnlyList<string> args)
        {
            CLIGeneratorOptions result = new();
            for (int i = 0; i < args.Count; i++)
            {
                string argument = args[i];
                if (argument.StartsWith("--output-dir=", StringComparison.Ordinal))
                    result.OutputDirectory = argument[13..];
                else if (argument is "-o" or "--output-dir")
                    result.OutputDirectory = ReadValue(args, ref i, argument);
                else if (argument.StartsWith("--targets=", StringComparison.Ordinal))
                    result.Targets = argument[10..];
                else if (argument == "--targets")
                    result.Targets = ReadValue(args, ref i, argument);
            }
            return result;
        }

        private static string ReadValue(IReadOnlyList<string> args, ref int index, string option)
        {
            if (++index >= args.Count)
                throw new ArgumentException($"Missing value for {option}.", nameof(args));
            return args[index];
        }
    }
}
