namespace BGCS.Cpp2C.Analysis
{
    using System.Text;

    /// <summary>
    /// Defines the public struct <c>CppIncludeBuilder</c>.
    /// </summary>
    internal sealed class CppIncludeBuilder
    {
        StringBuilder m_sb = new();
        /// <summary>
        /// Initializes a new instance of <see cref = "CppIncludeBuilder"/>.
        /// </summary>
        public CppIncludeBuilder()
        {
        }

        /// <summary>
        /// Executes public operation <c>Create</c>.
        /// </summary>
        public static CppIncludeBuilder Create()
        {
            return new CppIncludeBuilder();
        }

        /// <summary>
        /// Adds data or behavior through <c>AddSystemInclude</c>.
        /// </summary>
        public CppIncludeBuilder AddSystemInclude(string include)
        {
            this.m_sb.AppendLine($"#include <{include}>");
            return this;
        }

        /// <summary>
        /// Adds data or behavior through <c>AddInclude</c>.
        /// </summary>
        public CppIncludeBuilder AddInclude(string include)
        {
            this.m_sb.AppendLine($"#include \"{include}\"");
            return this;
        }

        /// <summary>
        /// Executes public operation <c>Build</c>.
        /// </summary>
        public string Build()
        {
            this.m_sb.AppendLine();
            var result = this.m_sb.ToString();
            this.m_sb.Clear();
            return result;
        }
    }
}
