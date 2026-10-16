using System.Collections.Immutable;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Requests;
using BGCS.Infrastructure.Execution;

namespace BGCS.NativeBuild.Validation;

/// <summary>Retains the actual native literal proof input and invocation independently from generated constant evidence.</summary>
/// <param name="command">The exact headerless compiler invocation, including execution-character options.</param>
/// <param name="source">The complete UTF-8 proof source retained after its private temporary file is deleted.</param>
/// <param name="sourceDigest">The digest of the actual compiler-read proof input.</param>
/// <param name="verifiedConstants">Selected source identities checked in the same order as the proof assertions.</param>
/// <param name="argumentInputs">Frozen nested response and configuration inputs expanded for this invocation.</param>
/// <param name="execution">The actual completed native compiler result.</param>
public sealed record NativeStringProofReceipt(
    NativeBuildCommand command,
    string source,
    ContentDigest sourceDigest,
    ImmutableArray<StableId> verifiedConstants,
    SourceSnapshot argumentInputs,
    ProcessExecutionResult execution
);
