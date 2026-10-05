using BGCS.Language.Lexing;
using BGCS.Language.Syntax;
namespace BGCS.Language.CSharp.Nodes
{


    /// <summary>
    /// Retains a mutable field declaration projection and its uncompiled initializer spelling.
    /// </summary>
    public class FieldNode : SyntaxNode
    {
        /// <summary>
        /// Retains the field declaration spelling and borrowed modifier array without compiling the initializer.
        /// </summary>
        /// <param name="type">
        /// The declared type spelling.
        /// </param>
        /// <param name="name">
        /// The field identifier.
        /// </param>
        /// <param name="modifiers">
        /// The modifier array retained without copying.
        /// </param>
        /// <param name="expression">
        /// The initializer spelling, or null when no initializer exists.
        /// </param>
        public FieldNode(
            string type,
            string name,
            KeywordType[] modifiers,
            string? expression
        ) {
            this.type = type;
            this.name = name;
            this.modifiers = modifiers;
            this.expression = expression;
        }

        /// <summary>
        /// Gets or sets the field's declared type spelling.
        /// </summary>
        public string type { get; set; }
        /// <summary>
        /// Gets or sets the field identifier.
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// Gets the retained mutable declaration modifier array.
        /// </summary>
        public KeywordType[] modifiers { get; }
        /// <summary>
        /// Gets or sets the uncompiled initializer spelling, or null when absent.
        /// </summary>
        public string? expression { get; set; }

        /// <summary>
        /// Formats the current field projection for syntax-tree diagnostics.
        /// </summary>
        /// <returns>
        /// The field label, modifiers, declared type, identifier, and initializer spelling.
        /// </returns>
        public override string ToString()
        {
            return $"field: {string.Join(" ", this.modifiers)} {this.type} {this.name} = {this.expression}";
        }
    }
}
