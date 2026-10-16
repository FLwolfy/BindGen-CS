using BGCS.Contracts.Inputs;
using BGCS.Contracts.Targets;

namespace BGCS.Contracts.Requests;

/// <summary>Supplies immutable target and source facts to neutral analysis extensions.</summary>
/// <param name="target">The resolved native target.</param>
/// <param name="language">The source language.</param>
/// <param name="snapshot">The verified frontend input snapshot.</param>
public sealed record AnalysisContext(
    NativeTargetDescriptor target,
    SourceLanguage language,
    SourceSnapshot snapshot
);
