using System.Collections.Immutable;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Requests;
using BGCS.Infrastructure.Execution;
using BGCS.NativeBuild.Execution;

namespace BGCS.NativeBuild.Validation;

/// <summary>Records three constexpr traits acquired from the selected native compiler, without replacing native type or layout facts.</summary>
/// <param name="declaration">The exact complete record declaration queried in the frozen module.</param>
/// <param name="isDefaultConstructible">Whether the native compiler accepts construction without arguments.</param>
/// <param name="isStandardLayout">The native compiler's standard-layout classification.</param>
/// <param name="isTriviallyCopyable">The native compiler's trivially-copyable classification.</param>
public sealed record NativeRecordTraitFact(
    StableId declaration,
    bool isDefaultConstructible,
    bool isStandardLayout,
    bool isTriviallyCopyable
);

/// <summary>Retains authenticated per-execution evidence; raw object hashes and elapsed times are not stable generation metadata.</summary>
/// <param name="recipeRevision">The symbol-size query protocol revision.</param>
/// <param name="source">The exact compiler-read source, including the completed native preprocessing output.</param>
/// <param name="sourceDigest">The exact compiler-read source digest.</param>
/// <param name="objectDigest">The target object digest authenticated before and after symbol inspection.</param>
/// <param name="plan">The exact object-only plan; no final linked library is produced.</param>
/// <param name="execution">Completed object compilation and dependency verification.</param>
/// <param name="inspectionCommand">The pinned symbol inspector and immutable arguments.</param>
/// <param name="inspection">The completed symbol inspector result.</param>
public sealed record NativeRecordTraitEvidence(
    int recipeRevision,
    string source,
    ContentDigest sourceDigest,
    ContentDigest objectDigest,
    NativeBuildPlan plan,
    NativeBuildExecutionResult execution,
    NativeBuildCommand inspectionCommand,
    ProcessExecutionResult inspection
);

/// <summary>Returns selected-native traits and external input obligations after private query files have been verified and removed.</summary>
/// <param name="facts">The exact queried declaration identities and native boolean values.</param>
/// <param name="inputs">External file, absence and search observations; original caller-owned plan exclusions remain unchanged.</param>
/// <param name="observationFingerprint">A stable query identity that must be combined with the caller's independently authenticated complete source-view identity.</param>
/// <param name="evidence">Per-execution evidence, or null when the module has no eligible record trait queries.</param>
/// <remarks>The query fingerprint does not replace the original token, macro, dynamic-header or compiler observation identity.</remarks>
public sealed record NativeRecordTraitObservation(
    ImmutableArray<NativeRecordTraitFact> facts,
    ImmutableArray<SourceSnapshot> inputs,
    ContentDigest observationFingerprint,
    NativeRecordTraitEvidence? evidence
);
