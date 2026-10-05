using BGCS.Language.Syntax;

namespace BGCS.Language.Cpp.Nodes;

/// <summary>
/// Retains a macro function-call identifier and its ordered argument expression children.
/// </summary>
public class FunctionCallNode : SyntaxNode
{
    /// <summary>
    /// Retains the parsed spelling and initializes an empty expression child sequence.
    /// </summary>
    /// <param name="name">
    /// The referenced function identifier.
    /// </param>
    public FunctionCallNode(string name)
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
    /// The call label and retained function identifier.
    /// </returns>
    public override string ToString()
    {
        return $"call: {this.name}";
    }
}
