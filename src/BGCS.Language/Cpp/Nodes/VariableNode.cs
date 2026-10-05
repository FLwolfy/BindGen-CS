using BGCS.Language.Syntax;

namespace BGCS.Language.Cpp.Nodes;

/// <summary>
/// Retains an identifier referenced by a macro expression.
/// </summary>
public class VariableNode : SyntaxNode
{
    /// <summary>
    /// Retains the parsed spelling and initializes an empty expression child sequence.
    /// </summary>
    /// <param name="name">
    /// The referenced identifier.
    /// </param>
    public VariableNode(string name)
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
    /// The variable label and retained identifier.
    /// </returns>
    public override string ToString()
    {
        return $"var: {this.name}";
    }
}
