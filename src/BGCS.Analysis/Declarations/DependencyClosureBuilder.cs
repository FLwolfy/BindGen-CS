using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Core.Common;

namespace BGCS.Analysis.Declarations;

/// <summary>Expands root declarations without dropping types required by their native ABI.</summary>
public static class DependencyClosureBuilder
{
    /// <summary>Expands stable root identities and orders their complete dependency closure.</summary>
    /// <param name="graph">The frozen native dependency graph.</param>
    /// <param name="roots">User-selected declarations.</param>
    /// <returns>An immutable, deterministically ordered closure.</returns>
    public static ImmutableArray<StableId> Build(
        DeclarationGraph graph,
        IEnumerable<StableId> roots
    ) {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(roots);
        HashSet<StableId> closure = [];
        Stack<StableId> pending = new(roots.OrderByDescending(id => id.value, StringComparer.Ordinal));
        while (pending.TryPop(out StableId id))
        {
            if (!closure.Add(id))
                continue;
            foreach (StableId dependency in graph.GetDependencies(id))
                pending.Push(dependency);
        }
        return graph.Order(closure);
    }
}
