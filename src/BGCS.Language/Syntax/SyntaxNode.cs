using System;
using System.Collections.Generic;
using System.Text;

namespace BGCS.Language.Syntax;

/// <summary>
/// Owns the ordered child sequence of a mutable authoring syntax node.
/// </summary>
/// <remarks>Nodes are edited by a single parser operation; concurrent mutation is unsupported.</remarks>
public abstract class SyntaxNode
{
    /// <summary>The mutable child storage available to syntax-node implementations.</summary>
    protected readonly List<SyntaxNode> m_children;
    private readonly IReadOnlyList<SyntaxNode> m_childrenView;

    /// <summary>Creates a node with no children.</summary>
    protected SyntaxNode() : this([]) { }

    /// <summary>Copies child references into a new list owned by this node.</summary>
    /// <param name="children">The initial children in source order; the caller's list is not retained.</param>
    /// <exception cref="ArgumentNullException">The initial list is null.</exception>
    protected SyntaxNode(List<SyntaxNode> children)
    {
        ArgumentNullException.ThrowIfNull(children);
        m_children = new(children);
        m_childrenView = m_children.AsReadOnly();
    }

    /// <summary>Gets a live, read-only view of this node's children in source order.</summary>
    public IReadOnlyList<SyntaxNode> children => m_childrenView;

    /// <summary>Appends a child reference without changing existing positions.</summary>
    /// <param name="node">The non-null child belonging to an acyclic syntax tree.</param>
    /// <exception cref="ArgumentNullException">The child is null.</exception>
    public void AddChild(SyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        m_children.Add(node);
    }

    /// <summary>Returns the child at a zero-based source-order position.</summary>
    /// <param name="index">The position within the child sequence.</param>
    /// <returns>The retained child reference.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The position lies outside the child sequence.</exception>
    public SyntaxNode GetChild(int index) => m_children[index];

    /// <summary>Removes the first matching child reference, leaving an absent child unchanged.</summary>
    /// <param name="node">The child reference to remove.</param>
    public void RemoveChild(SyntaxNode node) => m_children.Remove(node);

    /// <summary>Removes one child and shifts subsequent children one position toward the start.</summary>
    /// <param name="index">The zero-based child position to remove.</param>
    /// <exception cref="ArgumentOutOfRangeException">The position lies outside the child sequence.</exception>
    public void RemoveChildAt(int index) => m_children.RemoveAt(index);

    /// <summary>Checks whether the child sequence retains the supplied node.</summary>
    /// <param name="node">The node to locate using list equality.</param>
    /// <returns>True when a child matches; otherwise false.</returns>
    public bool Contains(SyntaxNode node) => m_children.Contains(node);

    /// <summary>Appends a tab-indented diagnostic representation of this node and its descendants.</summary>
    /// <param name="sb">The caller-owned output builder to append to.</param>
    /// <param name="level">The non-negative initial indentation, restored after the traversal completes.</param>
    /// <exception cref="ArgumentNullException">The output builder is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The indentation level is negative.</exception>
    public void BuildDebugTree(
        StringBuilder sb,
        ref int level
    ) {
        ArgumentNullException.ThrowIfNull(sb);
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        sb.Append('\t', level);
        sb.AppendLine(ToString());
        level++;
        try
        {
            foreach (SyntaxNode child in m_children)
                child.BuildDebugTree(sb, ref level);
        }
        finally
        {
            level--;
        }
    }
}
