namespace BGCS.Language
{
    using System.Collections.Generic;

    /// <summary>
    /// Defines the public class <c>RootNode</c>.
    /// </summary>
    public class RootNode : SyntaxNode
    {
        /// <summary>
        /// Initializes a new instance of <see cref="RootNode"/>.
        /// </summary>
        public RootNode()
        {
        }

        /// <summary>
        /// Executes public operation <c>RootNode</c>.
        /// </summary>
        public RootNode(List<SyntaxNode> children) : base(children)
        {
        }

        /// <summary>
        /// Executes public operation <c>ToString</c>.
        /// </summary>
        public override string ToString()
        {
            return $"root";
        }
    }
}
