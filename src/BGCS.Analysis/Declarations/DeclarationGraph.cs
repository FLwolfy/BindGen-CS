using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Contracts.Common;
using BGCS.Contracts.Native;

namespace BGCS.Analysis.Declarations;

/// <summary>Captures native declaration dependencies using only frozen module identities.</summary>
public sealed class DeclarationGraph
{
    private readonly FrozenDictionary<StableId, ImmutableArray<StableId>> m_edges;

    private DeclarationGraph(
        NativeModule module,
        FrozenDictionary<StableId, ImmutableArray<StableId>> edges
    ) {
        this.module = module;
        m_edges = edges;
    }

    /// <summary>Gets the immutable source module whose identities this graph references.</summary>
    public NativeModule module { get; }

    /// <summary>Builds edges for fields, parameters, aliases, returns, base classes and nested declarations.</summary>
    /// <param name="module">A previously verified native module.</param>
    /// <returns>A detached dependency graph.</returns>
    public static DeclarationGraph Create(NativeModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        Dictionary<StableId, ImmutableArray<StableId>> edges = [];
        foreach (NativeDeclaration declaration in module.declarations)
        {
            HashSet<StableId> dependencies = declaration.children.Where(child =>
                module.GetDeclaration(child).kind is "Field" or "Parameter" or "EnumItem"
                || declaration.objectiveCContainer is not null && module.GetDeclaration(child).kind is ("Property" or "Function")).ToHashSet();
            dependencies.UnionWith(ObjectiveCFactDependencies.Declarations(declaration));
            HashSet<StableId> types = [];
            if (declaration.parent is StableId parent && module.GetDeclaration(parent).kind != "Namespace")
                dependencies.Add(parent);
            AddType(declaration.type);
            AddType(declaration.sourceType);
            AddType(declaration.returnType);
            foreach (NativeBaseClass baseClass in declaration.baseClasses)
                AddType(baseClass.type);
            foreach (NativeTemplateArgument argument in declaration.templateArguments)
                AddType(argument.type);
            foreach (StableId parameter in declaration.templateParameters)
                AddType(parameter);
            if (declaration.objectiveCContainer is { } objectiveC)
                foreach (StableId parameter in objectiveC.genericParameters) AddType(parameter);
            dependencies.Remove(declaration.id);
            edges.Add(declaration.id, dependencies.OrderBy(id => id.value, StringComparer.Ordinal).ToImmutableArray());

            void AddType(StableId? typeId)
            {
                Stack<StableId> pending = new();
                if (typeId is StableId initial)
                    pending.Push(initial);
                while (pending.TryPop(out StableId id))
                {
                    if (!types.Add(id))
                        continue;
                    NativeType type = module.GetType(id);
                    if (type.declaration is StableId owner)
                        dependencies.Add(owner);
                    if (type.elementType is StableId element)
                        pending.Push(element);
                    if (type.canonicalType is StableId canonical)
                        pending.Push(canonical);
                    if (type.returnType is StableId result)
                        pending.Push(result);
                    foreach (StableId parameter in type.parameterTypes)
                        pending.Push(parameter);
                }
            }
        }
        return new(module, edges.ToFrozenDictionary());
    }

    /// <summary>Gets the deterministic declaration dependencies of one node.</summary>
    /// <param name="id">A declaration identity in the source module.</param>
    /// <returns>The immutable dependency identity sequence.</returns>
    public ImmutableArray<StableId> GetDependencies(StableId id) => m_edges[id];

    /// <summary>Orders dependencies before users where possible, while terminating on recursive native shapes.</summary>
    /// <param name="selected">The selected declaration closure.</param>
    /// <returns>A deterministic order. Cyclic native types still require forward declarations.</returns>
    public ImmutableArray<StableId> Order(IEnumerable<StableId> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        HashSet<StableId> selection = selected.ToHashSet();
        HashSet<StableId> entered = [];
        ImmutableArray<StableId>.Builder result = ImmutableArray.CreateBuilder<StableId>();
        Stack<(StableId Id, bool Exit)> pending = new();
        foreach (StableId root in selection.OrderBy(id => id.value, StringComparer.Ordinal))
        {
            pending.Push((root, false));
            while (pending.TryPop(out var step))
            {
                if (step.Exit)
                {
                    result.Add(step.Id);
                    continue;
                }
                if (!entered.Add(step.Id))
                    continue;
                pending.Push((step.Id, true));
                foreach (StableId dependency in m_edges[step.Id].Reverse())
                    if (selection.Contains(dependency))
                        pending.Push((dependency, false));
            }
        }
        return result.ToImmutable();
    }
}
