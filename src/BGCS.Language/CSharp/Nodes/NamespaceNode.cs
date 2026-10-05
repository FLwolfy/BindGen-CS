using BGCS.Language.Syntax;
namespace BGCS.Language.CSharp.Nodes;

using System.Collections.Generic;

/// <summary>
/// Groups namespace member nodes under one retained namespace spelling.
/// </summary>
public class NamespaceNode : SyntaxNode
{
    /// <summary>
    /// Retains a namespace spelling and initializes an empty member sequence.
    /// </summary>
    /// <param name="namespace">
    /// The declared namespace spelling.
    /// </param>
    public NamespaceNode(string @namespace)
    {
        this.@namespace = @namespace;
    }

    /// <summary>
    /// Retains namespace spelling and copies initial member references into an owned child container.
    /// </summary>
    /// <param name="namespace">
    /// The declared namespace spelling.
    /// </param>
    /// <param name="children">
    /// The initial members in source order; their list is copied.
    /// </param>
    public NamespaceNode(
        string @namespace,
        List<SyntaxNode> children
    ) : base(children)
    {
        this.@namespace = @namespace;
    }

    /// <summary>
    /// Gets the retained namespace spelling.
    /// </summary>
    public string @namespace { get; }

    /// <summary>
    /// Formats the namespace spelling for syntax-tree diagnostics.
    /// </summary>
    /// <returns>
    /// The namespace label followed by its retained spelling.
    /// </returns>
    public override string ToString()
    {
        return $"namespace: {this.@namespace}";
    }
}
