using BGCS.Core.Inputs;
using BGCS.Core.Targets;

namespace BGCS.Core.Requests;

/// <summary>Supplies immutable target and source facts to neutral analysis extensions.</summary>
/// <param name="target">The resolved native target.</param>
/// <param name="language">The source language.</param>
/// <param name="snapshot">The verified frontend input snapshot.</param>
public sealed record AnalysisContext(
    NativeTargetDescriptor target,
    SourceLanguage language,
    SourceSnapshot snapshot
);
