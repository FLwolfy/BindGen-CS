using System;
using BGCS.Core.Common;

namespace BGCS.Core.Native;

/// <summary>Provides a parser-independent view over one type in a frozen native module.</summary>
public readonly record struct NativeTypeView
{
    /// <summary>Creates a view and verifies that its type belongs to the module.</summary>
    /// <param name="module">The frozen native module.</param>
    /// <param name="id">The type identity to resolve.</param>
    /// <exception cref="ArgumentNullException">The module is null.</exception>
    public NativeTypeView(
        NativeModule module,
        StableId id
    ) {
        ArgumentNullException.ThrowIfNull(module);
        _ = module.GetType(id);
        this.module = module;
        this.id = id;
    }

    /// <summary>Gets the owning frozen module.</summary>
    public NativeModule module { get; }
    /// <summary>Gets the type identity.</summary>
    public StableId id { get; }
    /// <summary>Gets detached type facts.</summary>
    public NativeType type => module.GetType(id);
    /// <summary>Gets the underlying type view, or null for a type without an element.</summary>
    public NativeTypeView? element => type.elementType is StableId elementId ? new(module, elementId) : null;
    /// <summary>Gets the named declaration, or null for a type without a declaration.</summary>
    public NativeDeclaration? declaration => type.declaration is StableId declarationId ? module.GetDeclaration(declarationId) : null;
}
