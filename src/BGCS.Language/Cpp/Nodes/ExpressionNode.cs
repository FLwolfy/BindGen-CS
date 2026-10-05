using BGCS.Language.Syntax;

namespace BGCS.Language.Cpp.Nodes;

/// <summary>
/// Groups one parsed macro expression under a document-level expression root.
/// </summary>
public class ExpressionNode : SyntaxNode
{
    /// <summary>
    /// Formats this node's role and retained spelling for syntax-tree diagnostics.
    /// </summary>
    /// <returns>
    /// The expression-root label.
    /// </returns>
    public override string ToString()
    {
        return "expr";
    }
}
