using System;
using BGCS.Core.Native;

namespace BGCS.Analysis.Bridge;

/// <summary>Identifies C source callables that require a compiled adapter instead of an assumed linker export.</summary>
public static class CHeaderFunctionPolicy
{
    /// <summary>Checks source linkage, inline definition and compiler addressability facts.</summary>
    /// <param name="declaration">The exact detached native declaration.</param>
    /// <returns>Whether a selected C callable requires an explicitly requested native adapter.</returns>
    public static bool RequiresAdapter(NativeDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        return declaration.kind == "Function" && (declaration.isInline || declaration.isStatic
            || declaration.nativeLinkage == NativeLinkageKind.Internal || declaration.addressability == NativeCallableAddressability.DirectCallOnly);
    }
}
