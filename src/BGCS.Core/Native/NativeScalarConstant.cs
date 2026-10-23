using System.Collections.Immutable;
using BGCS.Core.Common;

namespace BGCS.Core.Native;

/// <summary>Identifies a compiler-established scalar constant representation.</summary>
public enum NativeScalarConstantKind
{
    /// <summary>An integral value with an independently established width and signedness.</summary>
    Integer,
    /// <summary>A native Boolean value rather than an arbitrary integer truth convention.</summary>
    Boolean,
    /// <summary>An IEEE binary32 value represented by its exact bits.</summary>
    Binary32,
    /// <summary>An IEEE binary64 value represented by its exact bits.</summary>
    Binary64
}

/// <summary>Preserves a typed scalar value without retaining compiler handles or managed source syntax.</summary>
/// <param name="kind">The established scalar representation.</param>
/// <param name="value">Canonical decimal integer/Boolean data or uppercase hexadecimal IEEE bits.</param>
/// <param name="width">The native scalar width in bytes.</param>
/// <param name="isSigned">Integral signedness, or null for other representations.</param>
/// <param name="evidence">The source of the compiler-derived value and representation facts.</param>
public sealed record NativeScalarConstant(
    NativeScalarConstantKind kind,
    string value,
    int width,
    bool? isSigned,
    Evidence evidence
);

/// <summary>Retains preprocessing identity independently of whether a replacement can become a scalar constant.</summary>
/// <param name="isFunctionLike">Whether expansion requires a native macro argument list.</param>
/// <param name="parameters">Ordered macro parameters, empty for an object-like macro.</param>
/// <param name="isActive">Whether this is the active final definition, or null when that fact has not been established.</param>
public sealed record NativeMacroDefinition(
    bool isFunctionLike,
    ImmutableArray<string> parameters,
    bool? isActive
)
{
    /// <summary>Gets the exact free function referenced by an object-like replacement, separately from scalar values.</summary>
    public StableId? functionAlias { get; init; }
    /// <summary>Gets the compiler procedure that established the alias target.</summary>
    public Evidence? functionAliasEvidence { get; init; }
}
