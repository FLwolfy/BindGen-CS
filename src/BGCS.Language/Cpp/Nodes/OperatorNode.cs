using BGCS.Language.Syntax;

namespace BGCS.Language.Cpp.Nodes;

/// <summary>
/// Retains an operator spelling and its ordered operand expression children.
/// </summary>
public class OperatorNode : SyntaxNode
{
    /// <summary>
    /// Retains the parsed spelling and initializes an empty expression child sequence.
    /// </summary>
    /// <param name="name">
    /// The unary or binary operator spelling.
    /// </param>
    public OperatorNode(string name)
    {
        this.name = name;
    }

    /// <summary>
    /// Gets the spelling retained by this expression node.
    /// </summary>
    public string name { get; }

    /// <summary>
    /// Formats this node's role and retained spelling for syntax-tree diagnostics.
    /// </summary>
    /// <returns>
    /// The operator label and retained spelling.
    /// </returns>
    public override string ToString()
    {
        return $"op: {this.name}";
    }
}
