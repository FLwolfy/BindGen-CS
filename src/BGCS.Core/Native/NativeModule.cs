using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json.Serialization;
using BGCS.Core.Common;
using BGCS.Core.Inputs;

namespace BGCS.Core.Native;

/// <summary>Owns a frozen native fact graph whose lifetime is independent of the parser process.</summary>
public sealed class NativeModule
{
    private readonly FrozenDictionary<StableId, NativeType> m_types;
    private readonly FrozenDictionary<StableId, NativeDeclaration> m_declarations;

    /// <summary>Freezes fact collections and validates unique identities.</summary>
    /// <param name="targetId">The concrete native target identity.</param>
    /// <param name="triple">The resolved compiler target triple.</param>
    /// <param name="pointerSize">The target pointer size in bytes.</param>
    /// <param name="types">The immutable type facts.</param>
    /// <param name="declarations">The immutable declaration facts.</param>
    /// <param name="diagnostics">The detached parse diagnostics.</param>
    /// <exception cref="ArgumentException">An identity is empty or fact identities collide.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The pointer size is not positive.</exception>
    [JsonConstructor]
    public NativeModule(
        string targetId,
        string triple,
        int pointerSize,
        ImmutableArray<NativeType> types,
        ImmutableArray<NativeDeclaration> declarations,
        ImmutableArray<BindingDiagnostic> diagnostics
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(triple);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pointerSize);
        this.targetId = targetId;
        this.triple = triple;
        this.pointerSize = pointerSize;
        this.types = types.IsDefault ? [] : types;
        this.declarations = declarations.IsDefault ? [] : declarations;
        this.diagnostics = diagnostics.IsDefault ? [] : diagnostics;
        m_types = this.types.ToFrozenDictionary(type => type.id);
        m_declarations = this.declarations.ToFrozenDictionary(declaration => declaration.id);
    }

    /// <summary>Gets the native model wire revision.</summary>
    public int modelRevision => BGCS.Core.Versioning.ModelRevision.C_NATIVE;
    /// <summary>Gets the source language, or None for a synthetic graph without declared language provenance.</summary>
    public SourceLanguage language { get; init; }
    /// <summary>Gets the concrete native target identity.</summary>
    public string targetId { get; }
    /// <summary>Gets the compiler target triple.</summary>
    public string triple { get; }
    /// <summary>Gets the native pointer size in bytes.</summary>
    public int pointerSize { get; }
    /// <summary>Gets the resolved native storage byte order; Unknown is a missing fact, not a host fallback.</summary>
    public NativeByteOrder byteOrder { get; init; }
    /// <summary>Gets the immutable type graph.</summary>
    public ImmutableArray<NativeType> types { get; }
    /// <summary>Gets the immutable declaration graph.</summary>
    public ImmutableArray<NativeDeclaration> declarations { get; }
    /// <summary>Gets detached parse diagnostics.</summary>
    public ImmutableArray<BindingDiagnostic> diagnostics { get; }

    /// <summary>Resolves a type identity in this module.</summary>
    /// <param name="id">The type identity.</param>
    /// <returns>The frozen type fact.</returns>
    /// <exception cref="KeyNotFoundException">The identity is not in the module.</exception>
    public NativeType GetType(StableId id) => m_types[id];

    /// <summary>Resolves a declaration identity in this module.</summary>
    /// <param name="id">The declaration identity.</param>
    /// <returns>The frozen declaration fact.</returns>
    /// <exception cref="KeyNotFoundException">The identity is not in the module.</exception>
    public NativeDeclaration GetDeclaration(StableId id) => m_declarations[id];
}
