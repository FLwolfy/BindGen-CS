using BGCS.Language.Syntax;

namespace BGCS.Language.Cpp.Nodes;

/// <summary>
/// Retains a type spelling used by the macro expression model.
/// </summary>
public class TypeNode : SyntaxNode
{
    /// <summary>
    /// Retains the parsed spelling and initializes an empty expression child sequence.
    /// </summary>
    /// <param name="name">
    /// The type spelling.
    /// </param>
    public TypeNode(string name)
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
    /// The type label and retained spelling.
    /// </returns>
    public override string ToString()
    {
        return $"type: {this.name}";
    }
}
