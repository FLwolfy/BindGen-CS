using BGCS.Language.Syntax;

namespace BGCS.Language.Cpp.Nodes;

/// <summary>
/// Retains a parsed cast-type spelling and its operand expression child.
/// </summary>
public class CastNode : SyntaxNode
{
    /// <summary>
    /// Retains the parsed spelling and initializes an empty expression child sequence.
    /// </summary>
    /// <param name="name">
    /// The cast-type spelling.
    /// </param>
    public CastNode(string name)
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
    /// The cast label and retained type spelling.
    /// </returns>
    public override string ToString()
    {
        return $"cast: {this.name}";
    }
}
