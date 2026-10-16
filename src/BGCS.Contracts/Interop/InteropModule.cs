using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Contracts.Common;
using BGCS.Contracts.Native;

namespace BGCS.Contracts.Interop;

/// <summary>Owns final native ABI shapes independently of managed API presentation and source emission.</summary>
public sealed class InteropModule
{
    private readonly FrozenDictionary<StableId, InteropType> m_types;

    /// <summary>Freezes a verified native ABI module.</summary>
    /// <param name="libraryId">The library's stable logical identity.</param>
    /// <param name="targetId">The concrete native target identity.</param>
    /// <param name="pointerSize">The native pointer width.</param>
    /// <param name="types">The immutable ABI types.</param>
    /// <param name="functions">The immutable native exports.</param>
    /// <param name="diagnostics">ABI analysis diagnostics.</param>
    public InteropModule(
        string libraryId,
        string targetId,
        int pointerSize,
        ImmutableArray<InteropType> types,
        ImmutableArray<InteropFunction> functions,
        ImmutableArray<BindingDiagnostic> diagnostics
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pointerSize);
        this.libraryId = libraryId;
        this.targetId = targetId;
        this.pointerSize = pointerSize;
        this.types = types.IsDefault ? [] : types;
        this.functions = functions.IsDefault ? [] : functions;
        this.diagnostics = diagnostics.IsDefault ? [] : diagnostics;
        m_types = this.types.ToFrozenDictionary(type => type.id);
    }

    /// <summary>Gets the logical library identity.</summary>
    public string libraryId { get; }
    /// <summary>Gets the concrete native target.</summary>
    public string targetId { get; }
    /// <summary>Gets the target pointer size.</summary>
    public int pointerSize { get; }
    /// <summary>Gets the established native byte order preserved by semantic transformations.</summary>
    public NativeByteOrder byteOrder { get; init; }
    /// <summary>Gets immutable native ABI types.</summary>
    public ImmutableArray<InteropType> types { get; }
    /// <summary>Gets immutable native exports.</summary>
    public ImmutableArray<InteropFunction> functions { get; }
    /// <summary>Gets analysis diagnostics.</summary>
    public ImmutableArray<BindingDiagnostic> diagnostics { get; }
    /// <summary>Gets selected typed source constants without inventing native linker exports.</summary>
    public ImmutableArray<InteropConstant> constants { get; init; } = [];
    /// <summary>Gets source aliases of existing native functions, without duplicating their linker symbols.</summary>
    public ImmutableArray<InteropFunctionAlias> functionAliases { get; init; } = [];
    /// <summary>Gets exact selected literal units without assuming ordinary bytes have a Unicode encoding.</summary>
    public ImmutableArray<InteropStringConstant> stringConstants { get; init; } = [];
    /// <summary>Gets checked native virtual proxy protocols without inventing caller retention or quiescence guarantees.</summary>
    public ImmutableArray<VirtualProxyContract> virtualProxies { get; init; } = [];
    /// <summary>Resolves a native ABI type identity.</summary>
    /// <param name="id">The native ABI type identity.</param>
    /// <returns>The corresponding immutable type.</returns>
    /// <exception cref="KeyNotFoundException">The identity is not in this module.</exception>
    public InteropType GetType(StableId id) => m_types[id];
}
