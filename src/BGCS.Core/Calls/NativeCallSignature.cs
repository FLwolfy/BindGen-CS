using System.Collections.Immutable;
using System;
using System.Linq;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;

namespace BGCS.Core.Calls;

/// <summary>Identifies the direction of a physical native call.</summary>
public enum NativeCallDirection
{
    /// <summary>Managed code calls an exported native function.</summary>
    Forward,
    /// <summary>Native code calls a managed callback thunk.</summary>
    ReverseCallback
}

/// <summary>Captures a complete physical signature independently of spelling or services.</summary>
/// <param name="id">The export or callback type identity.</param>
/// <param name="returnType">The original result identity.</param>
/// <param name="parameterTypes">Ordered original parameter identities.</param>
/// <param name="callingConvention">The established convention.</param>
/// <param name="direction">The physical call direction.</param>
/// <param name="isVariadic">Whether undeclared trailing arguments are accepted.</param>
public sealed record NativeCallSignature(
    StableId id,
    StableId returnType,
    ImmutableArray<StableId> parameterTypes,
    string callingConvention,
    NativeCallDirection direction,
    bool isVariadic
)
{
    /// <summary>Gets whether all native parameter types are specified by a complete prototype.</summary>
    public bool hasPrototype { get; init; } = true;
    /// <summary>Captures the exact physical signature of an exported function.</summary>
    /// <param name="function">The immutable final export.</param>
    /// <returns>The forward signature.</returns>
    public static NativeCallSignature FromFunction(InteropFunction function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return new(function.id, function.returnType, function.parameters.Select(parameter => parameter.type).ToImmutableArray(),
            function.callingConvention, NativeCallDirection.Forward, function.isVariadic)
        { hasPrototype = function.hasPrototype };
    }

    /// <summary>Captures a detached callback prototype for reverse-call assessment.</summary>
    /// <param name="type">The exact underlying function type.</param>
    /// <returns>The reverse signature.</returns>
    public static NativeCallSignature FromCallback(InteropType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type.category != NativeTypeCategory.Function || type.returnType is not StableId result || !type.hasPrototype)
            throw new ArgumentException("A callback assessment requires a complete function prototype.", nameof(type));
        return new(type.id, result, type.parameterTypes, type.callingConvention ?? string.Empty, NativeCallDirection.ReverseCallback, type.isVariadic);
    }
}
