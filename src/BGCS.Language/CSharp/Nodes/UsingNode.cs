using BGCS.Language.Syntax;
namespace BGCS.Language.CSharp.Nodes
{


    /// <summary>
    /// Retains the namespace spelling of one parsed namespace-import declaration.
    /// </summary>
    public class UsingNode : SyntaxNode
    {
        /// <summary>
        /// Retains a namespace-import spelling without resolving its referenced assembly.
        /// </summary>
        /// <param name="using">
        /// The imported namespace spelling.
        /// </param>
        public UsingNode(string @using)
        {
            this.@using = @using;
        }

        /// <summary>
        /// Gets the retained imported namespace spelling.
        /// </summary>
        public string @using { get; }

        /// <summary>
        /// Formats the namespace-import spelling for syntax-tree diagnostics.
        /// </summary>
        /// <returns>
        /// The using label followed by the retained namespace spelling.
        /// </returns>
        public override string ToString()
        {
            return $"using: {this.@using}";
        }
    }
}
