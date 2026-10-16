using System;
using System.Collections.Generic;
using System.Linq;
using BGCS.Contracts.Common;
using BGCS.Contracts.Native;

namespace BGCS.Analysis.Declarations;

/// <summary>Finds detached type provenance through declaration usage and neutral type edges.</summary>
public sealed class NativeSourceLocator
{
    private readonly Dictionary<StableId, SourceLocation> m_sources = [];

    /// <summary>Builds a deterministic nearest-usage index without retaining parser handles.</summary>
    /// <param name="module">The frozen native declaration and type graph.</param>
    public NativeSourceLocator(NativeModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        Queue<StableId> pending = new();
        // A named type's own declaration is more precise than any use of it.
        foreach (NativeType type in module.types.OrderBy(type => type.id.value, StringComparer.Ordinal))
            if (type.declaration is StableId declaration) Add(type.id, module.GetDeclaration(declaration).source);
        // Prefer application declarations to SDK uses and stabilize ties by identity.
        foreach (NativeDeclaration declaration in module.declarations.OrderBy(declaration => declaration.isSystemDeclaration)
            .ThenBy(declaration => declaration.id.value, StringComparer.Ordinal))
        {
            if (declaration.type is StableId type) Add(type, declaration.source);
            if (declaration.returnType is StableId result) Add(result, declaration.source);
            if (declaration.sourceType is StableId written) Add(written, declaration.source);
        }
        // Multi-source breadth first propagation visits each type only once.
        // Aliases/canonical types and callback shapes share provenance without
        // repeating a traversal of the entire graph for every declaration.
        while (pending.TryDequeue(out StableId id))
        {
            NativeType type = module.GetType(id);
            SourceLocation source = m_sources[id];
            if (type.elementType is StableId element) Add(element, source);
            if (type.canonicalType is StableId canonical) Add(canonical, source);
            if (type.returnType is StableId result) Add(result, source);
            foreach (StableId parameter in type.parameterTypes) Add(parameter, source);
        }

        void Add(
            StableId type,
            SourceLocation source
        ) {
            if (string.IsNullOrWhiteSpace(source.file) || source.line <= 0 || source.column <= 0) return;
            if (m_sources.TryAdd(type, source)) pending.Enqueue(type);
        }
    }

    /// <summary>Gets an original declaration or nearest usage position when one is known.</summary>
    /// <param name="type">The neutral native type identity.</param>
    /// <returns>The detached location, or null when the graph has no source evidence.</returns>
    public SourceLocation? Locate(StableId type) => m_sources.TryGetValue(type, out SourceLocation source) ? source : null;
}
