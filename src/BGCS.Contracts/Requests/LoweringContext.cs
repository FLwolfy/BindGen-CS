using System.Collections.Immutable;
using BGCS.Contracts.Native;
using BGCS.Contracts.Targets;

namespace BGCS.Contracts.Requests;

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
);
