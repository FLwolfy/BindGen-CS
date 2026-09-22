namespace BGCS.Intermediate;

/// <summary>
/// Identifies the severity of a structured generation diagnostic.
/// </summary>
public enum BindingDiagnosticSeverity
{
    Trace,
    Debug,
    Information,
    Warning,
    Error,
    Critical
}

/// <summary>
/// Represents one frontend, analysis, emission, or validation diagnostic.
/// </summary>
/// <param name="Severity">Diagnostic severity.</param>
/// <param name="Message">Actionable diagnostic text.</param>
/// <param name="Code">Stable diagnostic code when available.</param>
public sealed record BindingDiagnostic(BindingDiagnosticSeverity Severity, string Message, string? Code = null);

/// <summary>
/// Reports the analyzed module, emitted files, diagnostics, and success state of one generation run.
/// </summary>
public sealed class BindingGenerationResult
{
    public BindingGenerationResult(BindingModule? module, bool success, IReadOnlyList<string> outputFiles,
        IReadOnlyList<BindingDiagnostic> diagnostics, bool cacheHit = false, string? cacheKey = null)
    {
        Module = module;
        Success = success;
        OutputFiles = outputFiles;
        Diagnostics = diagnostics;
        CacheHit = cacheHit;
        CacheKey = cacheKey;
    }

    public BindingModule? Module { get; }
    public bool Success { get; }
    public IReadOnlyList<string> OutputFiles { get; }
    public IReadOnlyList<BindingDiagnostic> Diagnostics { get; }
    /// <summary>Gets whether the complete output was restored from the content-addressed cache.</summary>
    public bool CacheHit { get; }
    /// <summary>Gets the content-addressed cache key when incremental caching participated in the run.</summary>
    public string? CacheKey { get; }
}
