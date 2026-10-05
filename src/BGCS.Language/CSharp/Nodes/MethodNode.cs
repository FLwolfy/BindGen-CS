using BGCS.Language.Lexing;
using BGCS.Language.Syntax;
namespace BGCS.Language.CSharp.Nodes
{
    /// <summary>
    /// Retains a supported method signature as identifier, return-type, modifier, and parameter spellings.
    /// </summary>
    public class MethodNode : SyntaxNode
    {
        /// <summary>
        /// Retains signature spelling and the caller-supplied modifier and parameter arrays without copying.
        /// </summary>
        /// <param name="name">
        /// The method identifier.
        /// </param>
        /// <param name="modifiers">
        /// The retained mutable declaration modifier array.
        /// </param>
        /// <param name="parameters">
        /// The retained parameter spelling array in declaration order.
        /// </param>
        /// <param name="returnType">
        /// The declared return-type spelling.
        /// </param>
        public MethodNode(
            string name,
            KeywordType[] modifiers,
            string[] parameters,
            string returnType
        ) {
            this.name = name;
            this.modifiers = modifiers;
            this.parameters = parameters;
            this.returnType = returnType;
        }

        /// <summary>
        /// Gets the retained method identifier.
        /// </summary>
        public string name { get; }
        /// <summary>
        /// Gets the retained mutable declaration modifier array.
        /// </summary>
        public KeywordType[] modifiers { get; }
        /// <summary>
        /// Gets the retained mutable parameter spelling array in declaration order.
        /// </summary>
        public string[] parameters { get; }
        /// <summary>
        /// Gets the retained return-type spelling.
        /// </summary>
        public string returnType { get; }

        /// <summary>
        /// Formats the retained method signature for syntax-tree diagnostics.
        /// </summary>
        /// <returns>
        /// The method label, modifiers, return type, identifier, and parameter spellings.
        /// </returns>
        public override string ToString()
        {
            return $"method: {string.Join(" ", this.modifiers)} {this.returnType} {this.name} ({string.Join(" ", this.parameters)})";
        }
    }
}
