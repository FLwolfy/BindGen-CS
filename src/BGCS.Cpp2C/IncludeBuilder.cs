namespace BGCS.Cpp2C
{
    using System.Diagnostics.CodeAnalysis;
    using System.Text;

    /// <summary>
    /// Defines the public struct <c>IncludeBuilder</c>.
    /// </summary>
    public struct IncludeBuilder
    {
        StringBuilder sb = new();

        /// <summary>
        /// Initializes a new instance of <see cref="IncludeBuilder"/>.
        /// </summary>
        public IncludeBuilder()
        {
        }

        /// <summary>
        /// Executes public operation <c>Create</c>.
        /// </summary>
        public static IncludeBuilder Create()
        {
            return new IncludeBuilder();
        }

        [UnscopedRef]
        /// <summary>
        /// Adds data or behavior through <c>AddSystemInclude</c>.
        /// </summary>
        public ref IncludeBuilder AddSystemInclude(string include)
        {
            sb.AppendLine($"#include <{include}>");
            return ref this;
        }

        [UnscopedRef]
        /// <summary>
        /// Adds data or behavior through <c>AddInclude</c>.
        /// </summary>
        public ref IncludeBuilder AddInclude(string include)
        {
            sb.AppendLine($"#include \"{include}\"");
            return ref this;
        }

        /// <summary>
        /// Executes public operation <c>Build</c>.
        /// </summary>
        public readonly string Build()
        {
            sb.AppendLine();
            var result = sb.ToString();
            sb.Clear();
            return result;
        }
    }
}
