using BGCS.Language.Syntax;

namespace BGCS.Language.Cpp.Nodes;

/// <summary>
/// Groups an expression enclosed in parentheses; the grouped expression is retained as a child.
/// </summary>
public class GroupNode : SyntaxNode
{
    /// <summary>
    /// Formats this node's role and retained spelling for syntax-tree diagnostics.
    /// </summary>
    /// <returns>
    /// The parenthesized-group label.
    /// </returns>
    public override string ToString()
    {
        return "group";
    }
}
