using System.Collections.Generic;
using BGCS.Language.Lexing;
using BGCS.Language.Syntax;

namespace BGCS.Language.CSharp.Nodes
{
    /// <summary>
    /// Retains a class identifier, declaration modifiers, and owned member child sequence.
    /// </summary>
    public class ClassNode : SyntaxNode
    {
        /// <summary>
        /// Retains the class declaration spelling and initializes an empty owned member sequence.
        /// </summary>
        /// <param name="name">
        /// The retained class identifier.
        /// </param>
        /// <param name="modifiers">
        /// The declaration modifier array retained without copying.
        /// </param>
        public ClassNode(
            string name,
            KeywordType[] modifiers
        ) {
            this.name = name;
            this.modifiers = modifiers;
        }

        /// <summary>
        /// Retains class spelling and copies initial member references into an owned child container.
        /// </summary>
        /// <param name="name">
        /// The retained class identifier.
        /// </param>
        /// <param name="modifiers">
        /// The declaration modifier array retained without copying.
        /// </param>
        /// <param name="children">
        /// The initial members in source order; their list is copied.
        /// </param>
        public ClassNode(
            string name,
            KeywordType[] modifiers,
            List<SyntaxNode> children
        ) : base(children)
        {
            this.name = name;
            this.modifiers = modifiers;
        }

        /// <summary>
        /// Gets the retained class identifier.
        /// </summary>
        public string name { get; }
        /// <summary>
        /// Gets the retained mutable declaration modifier array.
        /// </summary>
        public KeywordType[] modifiers { get; }

        /// <summary>
        /// Formats the class identifier and current modifiers for syntax-tree diagnostics.
        /// </summary>
        /// <returns>
        /// The class label, modifier sequence, and class identifier.
        /// </returns>
        public override string ToString()
        {
            return $"class: {string.Join(" ", this.modifiers)} {this.name}";
        }
    }
}
