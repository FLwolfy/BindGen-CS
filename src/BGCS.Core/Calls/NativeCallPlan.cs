using System.Collections.Immutable;
using BGCS.Core.Common;
using BGCS.Core.Native;
using BGCS.Core.Targets;

namespace BGCS.Core.Calls;

/// <summary>Records a carrier's checked storage and provider-defined calling classification.</summary>
/// <param name="nativeType">The exact original type identity.</param>
/// <param name="size">The positive native storage size.</param>
/// <param name="alignment">The positive native storage alignment.</param>
/// <param name="classes">The immutable calling classifications.</param>
public sealed record NativeCallValuePlan(
    StableId nativeType,
    long size,
    long alignment,
    ImmutableArray<string> classes
);

/// <summary>Freezes a complete physical signature and the facts on which direct calling depends.</summary>
/// <param name="signature">The exact signature, including direction and convention.</param>
/// <param name="nativeTargetId">The resolved native target.</param>
/// <param name="runtimeId">The managed execution identity.</param>
/// <param name="runtimeProfileId">The execution-capability profile identity.</param>
/// <param name="provider">The selected ABI implementation.</param>
/// <param name="pointerSize">The native pointer width.</param>
/// <param name="byteOrder">The native byte order.</param>
/// <param name="returnValue">The result carrier, or null for void.</param>
/// <param name="parameters">All carriers in physical parameter order.</param>
public sealed record NativeCallPlan(
    NativeCallSignature signature,
    string nativeTargetId,
    ManagedRuntimeId runtimeId,
    string runtimeProfileId,
    CallAbiId provider,
    int pointerSize,
    NativeByteOrder byteOrder,
    NativeCallValuePlan? returnValue,
    ImmutableArray<NativeCallValuePlan> parameters
)
{
    /// <summary>Gets the exact native target triple assessed by the provider.</summary>
    public string nativeTriple { get; init; } = string.Empty;
    /// <summary>Gets the complete execution-capability identity captured when this plan was assessed.</summary>
    public ContentDigest runtimeProfileDigest { get; init; }
}
