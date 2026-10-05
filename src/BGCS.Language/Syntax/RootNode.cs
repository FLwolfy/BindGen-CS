namespace BGCS.Language.Syntax
{
    using System.Collections.Generic;

    /// <summary>
    /// Groups top-level syntax declarations in their source order.
    /// </summary>
    public class RootNode : SyntaxNode
    {
        /// <summary>
        /// Creates a document root with no declarations.
        /// </summary>
        public RootNode()
        {
        }

        /// <summary>
        /// Copies initial declaration references into this root's owned sequence.
        /// </summary>
        /// <param name="children">
        /// The declarations in source order; the input list is not retained.
        /// </param>
        public RootNode(List<SyntaxNode> children) : base(children)
        {
        }

        /// <summary>
        /// Returns the document-root label used by syntax debug output.
        /// </summary>
        /// <returns>
        /// The literal root label.
        /// </returns>
        public override string ToString()
        {
            return $"root";
        }
    }
}
