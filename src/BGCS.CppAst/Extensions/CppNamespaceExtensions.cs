using System.Collections.Generic;
using System.Text;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Model.Metadata;

namespace BGCS.CppAst.Extensions;

/// <summary>
/// Traverses parsed C++ namespaces without introducing generator dependencies into the parser.
/// </summary>
public static class CppNamespaceExtensions
{
    /// <summary>
    /// Enumerates all nested namespaces in depth-first traversal order.
    /// </summary>
    /// <param name = "compilation">
    /// The parsed translation unit whose namespace tree is traversed.
    /// </param>
    /// <returns>
    /// A lazy sequence of namespaces; the translation unit retains ownership of their AST nodes.
    /// </returns>
    public static IEnumerable<CppNamespace> EnumerateNamespaces(this CppCompilation compilation)
    {
        Stack<CppNamespace> pending = new(compilation.namespaces);
        while (pending.TryPop(out CppNamespace? current))
        {
            yield return current;
            foreach (CppNamespace child in current.namespaces)
                pending.Push(child);
        }
    }

    /// <summary>
    /// Builds a namespace's qualified name by walking its namespace parents.
    /// </summary>
    /// <param name = "namespaceNode">
    /// The namespace whose ancestry is read.
    /// </param>
    /// <param name = "separator">
    /// The separator inserted between ancestor names.
    /// </param>
    /// <returns>
    /// The qualified name including the supplied namespace itself.
    /// </returns>
    public static string GetFullNamespace(
        this CppNamespace namespaceNode,
        string separator
    ) {
        StringBuilder name = new();
        ICppContainer? current = namespaceNode;
        while (current is CppNamespace ancestor)
        {
            if (name.Length > 0)
                name.Insert(0, separator);
            name.Insert(0, ancestor.name);
            current = ancestor.parent;
        }

        return name.ToString();
    }
}
