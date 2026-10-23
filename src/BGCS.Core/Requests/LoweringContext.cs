using System.Collections.Immutable;
using BGCS.Core.Native;
using BGCS.Core.Targets;
using BGCS.Core.Interop;

namespace BGCS.Core.Requests;

/// <summary>Supplies detached target and selection facts to C++ lowering extensions.</summary>
/// <param name="module">The immutable source-language graph.</param>
/// <param name="target">The concrete target and SDK facts.</param>
/// <param name="exportPrefix">The allocated C identifier prefix.</param>
/// <param name="headers">The logical native header inputs.</param>
public sealed record LoweringContext(
    NativeModule module,
    NativeTargetDescriptor target,
    string exportPrefix,
    ImmutableArray<string> headers
)
{
    /// <summary>Gets the selected C semantics before physical carrier lowering, or null for source-only C++ planning.</summary>
    public InteropModule? semantics { get; init; }
}
