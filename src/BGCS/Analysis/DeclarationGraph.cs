using System;
using System.Collections.Generic;
using System.Linq;

namespace BGCS.Analysis;

using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Model.Metadata;
using BGCS.CppAst.Model.Types;

/// <summary>
/// Represents one native declaration and the declarations required by its public ABI.
/// </summary>
public sealed class DeclarationGraphNode
{
    private readonly HashSet<DeclarationGraphNode> m_dependencies = [];
    private IReadOnlyList<DeclarationGraphNode> m_dependencySnapshot = [];
    internal DeclarationGraphNode(ICppDeclaration declaration)
    {
        this.declaration = declaration;
    }

    /// <summary>
    /// Gets the native declaration borrowed from the compilation used to create this graph.
    /// </summary>
    public ICppDeclaration declaration { get; }

    /// <summary>
    /// Gets the immutable declaration-dependency snapshot captured during graph construction.
    /// </summary>
    public IReadOnlyList<DeclarationGraphNode> dependencies => m_dependencySnapshot;

    internal void AddDependency(DeclarationGraphNode dependency) => m_dependencies.Add(dependency);

    internal void Freeze() => m_dependencySnapshot = Array.AsReadOnly(m_dependencies.ToArray());
}

/// <summary>
/// Builds a dependency graph from native declarations before any output-language emission occurs.
/// </summary>
public sealed class DeclarationGraph
{
    private readonly Dictionary<ICppDeclaration, DeclarationGraphNode> m_nodes = new(ReferenceEqualityComparer.Instance);
    private IReadOnlyList<DeclarationGraphNode> m_nodeSnapshot = [];

    private DeclarationGraph(int pointerSize) => this.pointerSize = pointerSize;

    /// <summary>
    /// Gets the native target's pointer width captured from the owning compilation, in bytes.
    /// </summary>
    public int pointerSize { get; }

    /// <summary>
    /// Gets the immutable node snapshot captured from the selected native declarations.
    /// </summary>
    public IReadOnlyList<DeclarationGraphNode> nodes => m_nodeSnapshot;

    /// <summary>
    /// Captures declarations and their ABI dependencies while retaining their parser model references.
    /// </summary>
    /// <param name="compilation">Compilation that owns the declarations; it must outlive graph analysis.</param>
    /// <param name="predicate">Optional declaration filter; null includes all declarations.</param>
    /// <returns>A completed graph whose dependency collections cannot be modified by consumers.</returns>
    /// <exception cref="ArgumentNullException">The compilation is null.</exception>
    public static DeclarationGraph Create(
        CppCompilation compilation,
        Func<ICppDeclaration, bool>? predicate = null
    ) {
        ArgumentNullException.ThrowIfNull(compilation);
        DeclarationGraph graph = new(compilation.pointerSize);
        graph.AddContainer(compilation, predicate ?? (_ => true));
        graph.ConnectDependencies();
        foreach (DeclarationGraphNode node in graph.m_nodes.Values)
            node.Freeze();
        graph.m_nodeSnapshot = Array.AsReadOnly(graph.m_nodes.Values.ToArray());
        return graph;
    }

    /// <summary>
    /// Finds a captured node by its native declaration instance.
    /// </summary>
    /// <param name="declaration">Declaration instance from the source compilation.</param>
    /// <param name="node">Matched node, or null when the declaration was excluded or absent.</param>
    /// <returns>True when a matching node was captured; otherwise false.</returns>
    /// <exception cref="ArgumentNullException">The declaration is null.</exception>
    public bool TryGetNode(
        ICppDeclaration declaration,
        out DeclarationGraphNode? node
    ) {
        ArgumentNullException.ThrowIfNull(declaration);
        return this.m_nodes.TryGetValue(declaration, out node);
    }

    /// <summary>
    /// Orders declarations after their dependencies, visiting each node once even in recursive type graphs.
    /// </summary>
    /// <returns>A newly allocated ordering; cyclic dependencies are visited without reporting a strict topological order.</returns>
    public IReadOnlyList<DeclarationGraphNode> TopologicalOrder()
    {
        List<DeclarationGraphNode> output = [];
        HashSet<DeclarationGraphNode> permanent = [];
        HashSet<DeclarationGraphNode> temporary = [];
        foreach (DeclarationGraphNode node in this.m_nodes.Values)
        {
            Visit(node);
        }

        return output;
        void Visit(DeclarationGraphNode node)
        {
            if (permanent.Contains(node))
                return;
            if (!temporary.Add(node))
                return;
            foreach (DeclarationGraphNode dependency in node.dependencies)
                Visit(dependency);
            temporary.Remove(node);
            permanent.Add(node);
            output.Add(node);
        }
    }

    private void AddContainer(
        ICppContainer container,
        Func<ICppDeclaration, bool> predicate
    ) {
        foreach (ICppDeclaration declaration in container.children)
        {
            if (predicate(declaration))
                this.m_nodes.TryAdd(declaration, new(declaration));
            if (declaration is ICppContainer nested)
                AddContainer(nested, predicate);
        }
    }

    private void ConnectDependencies()
    {
        foreach (DeclarationGraphNode node in this.m_nodes.Values)
        {
            switch (node.declaration)
            {
                case CppFunction function:
                    AddTypeDependency(node, function.returnType);
                    foreach (CppParameter parameter in function.parameters)
                        AddTypeDependency(node, parameter.type);
                    break;
                case CppClass cppClass:
                    foreach (CppBaseType baseType in cppClass.baseTypes)
                        AddTypeDependency(node, baseType.type);
                    foreach (CppField field in cppClass.fields)
                        AddTypeDependency(node, field.type);
                    break;
                case CppTypedef typedef:
                    AddTypeDependency(node, typedef.elementType);
                    break;
                case CppField field:
                    AddTypeDependency(node, field.type);
                    break;
            }
        }
    }

    private void AddTypeDependency(
        DeclarationGraphNode owner,
        CppType type
    ) {
        while (type is CppTypeWithElementType wrapper)
            type = wrapper.elementType;
        if (type is ICppDeclaration declaration && this.m_nodes.TryGetValue(declaration, out DeclarationGraphNode? dependency) && !ReferenceEquals(owner, dependency))
            owner.AddDependency(dependency);
        if (type is CppFunctionType functionType)
        {
            AddTypeDependency(owner, functionType.returnType);
            foreach (CppParameter parameter in functionType.parameters)
                AddTypeDependency(owner, parameter.type);
        }
    }
}
