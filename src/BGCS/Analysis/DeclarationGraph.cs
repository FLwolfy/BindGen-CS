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
    internal DeclarationGraphNode(ICppDeclaration declaration)
    {
        Declaration = declaration;
    }

    public ICppDeclaration Declaration { get; }
    public ISet<DeclarationGraphNode> Dependencies { get; } = new HashSet<DeclarationGraphNode>();
}

/// <summary>
/// Builds a dependency graph from native declarations before any output-language emission occurs.
/// </summary>
public sealed class DeclarationGraph
{
    private readonly Dictionary<ICppDeclaration, DeclarationGraphNode> nodes =
        new(ReferenceEqualityComparer.Instance);

    public IReadOnlyCollection<DeclarationGraphNode> Nodes => nodes.Values;

    public static DeclarationGraph Create(CppCompilation compilation, Func<ICppDeclaration, bool>? predicate = null)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        DeclarationGraph graph = new();
        graph.AddContainer(compilation, predicate ?? (_ => true));
        graph.ConnectDependencies();
        return graph;
    }

    public bool TryGetNode(ICppDeclaration declaration, out DeclarationGraphNode? node)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        return nodes.TryGetValue(declaration, out node);
    }

    public IReadOnlyList<DeclarationGraphNode> TopologicalOrder()
    {
        List<DeclarationGraphNode> output = [];
        HashSet<DeclarationGraphNode> permanent = [];
        HashSet<DeclarationGraphNode> temporary = [];
        foreach (DeclarationGraphNode node in nodes.Values)
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
            foreach (DeclarationGraphNode dependency in node.Dependencies)
                Visit(dependency);
            temporary.Remove(node);
            permanent.Add(node);
            output.Add(node);
        }
    }

    private void AddContainer(ICppContainer container, Func<ICppDeclaration, bool> predicate)
    {
        foreach (ICppDeclaration declaration in container.Children)
        {
            if (predicate(declaration))
                nodes.TryAdd(declaration, new(declaration));
            if (declaration is ICppContainer nested)
                AddContainer(nested, predicate);
        }
    }

    private void ConnectDependencies()
    {
        foreach (DeclarationGraphNode node in nodes.Values)
        {
            switch (node.Declaration)
            {
                case CppFunction function:
                    AddTypeDependency(node, function.ReturnType);
                    foreach (CppParameter parameter in function.Parameters)
                        AddTypeDependency(node, parameter.Type);
                    break;
                case CppClass cppClass:
                    foreach (CppBaseType baseType in cppClass.BaseTypes)
                        AddTypeDependency(node, baseType.Type);
                    foreach (CppField field in cppClass.Fields)
                        AddTypeDependency(node, field.Type);
                    break;
                case CppTypedef typedef:
                    AddTypeDependency(node, typedef.ElementType);
                    break;
                case CppField field:
                    AddTypeDependency(node, field.Type);
                    break;
            }
        }
    }

    private void AddTypeDependency(DeclarationGraphNode owner, CppType type)
    {
        while (type is CppTypeWithElementType wrapper)
            type = wrapper.ElementType;
        if (type is ICppDeclaration declaration && nodes.TryGetValue(declaration, out DeclarationGraphNode? dependency) &&
            !ReferenceEquals(owner, dependency))
            owner.Dependencies.Add(dependency);
        if (type is CppFunctionType functionType)
        {
            AddTypeDependency(owner, functionType.ReturnType);
            foreach (CppParameter parameter in functionType.Parameters)
                AddTypeDependency(owner, parameter.Type);
        }
    }
}
