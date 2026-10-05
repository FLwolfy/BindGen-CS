using System.Collections.Generic;

namespace BGCS.Language.Syntax
{
    using System.Text;

    /// <summary>
    /// Retains the root of a completed authoring syntax tree.
    /// </summary>
    public class SyntaxTree
    {
        private readonly RootNode m_root;
        /// <summary>
        /// Retains the parsed document root without copying its nodes.
        /// </summary>
        /// <param name="root">
        /// The document root owned by this parse result.
        /// </param>
        public SyntaxTree(RootNode root)
        {
            this.m_root = root;
        }

        /// <summary>
        /// Gets the root's read-only declaration sequence in source order.
        /// </summary>
        public IReadOnlyList<SyntaxNode> nodes => this.m_root.children;

        /// <summary>
        /// Formats the root and all descendants as a tab-indented diagnostic tree.
        /// </summary>
        /// <returns>
        /// The complete diagnostic representation, including platform line endings.
        /// </returns>
        public string BuildDebugTree()
        {
            StringBuilder sb = new();
            sb.AppendLine(this.m_root.ToString());
            var level = 1;
            for (int i = 0; i < this.m_root.children.Count; i++)
            {
                this.m_root.children[i].BuildDebugTree(sb, ref level);
            }

            return sb.ToString();
        }
    }
}
