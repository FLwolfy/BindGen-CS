using System;
using System.Collections.Generic;
using System.Linq;

namespace BGCS.Intermediate;

/// <summary>
/// Identifies the severity of a structured generation diagnostic.
/// </summary>
public enum BindingDiagnosticSeverity
{
    /// <summary>
    /// Fine-grained generation tracing.
    /// </summary>
    Trace,
    /// <summary>
    /// Generation details useful during investigation.
    /// </summary>
    Debug,
    /// <summary>
    /// Normal progress or contextual information.
    /// </summary>
    Information,
    /// <summary>
    /// A recoverable condition requiring review.
    /// </summary>
    Warning,
    /// <summary>
    /// A condition that prevents a supported generation result.
    /// </summary>
    Error,
    /// <summary>
    /// A failure that prevents the generation operation from continuing safely.
    /// </summary>
    Critical
}

/// <summary>
/// Represents one frontend, analysis, emission, or validation diagnostic.
/// </summary>
/// <param name = "severity">Diagnostic severity.</param>
/// <param name = "message">Actionable diagnostic text.</param>
/// <param name = "code">Stable diagnostic code when available.</param>
public sealed record BindingDiagnostic(
    BindingDiagnosticSeverity severity,
    string message,
    string? code = null
);
/// <summary>
/// Reports the analyzed module, emitted files, diagnostics, and success state of one generation run.
/// </summary>
/// <typeparam name = "TModule">
/// The frozen semantic model produced by the generation pipeline.
/// </typeparam>
public sealed class BindingGenerationResult<TModule>
    where TModule : class
{
    /// <summary>
    /// Captures the outcome of one attempt without retaining mutable caller collections.
    /// </summary>
    /// <param name="module">
    /// Frozen semantic model, or null when unavailable.
    /// </param>
    /// <param name="success">
    /// Whether every required stage succeeded.
    /// </param>
    /// <param name="outputFiles">
    /// Emitted or restored file paths copied into the result.
    /// </param>
    /// <param name="diagnostics">
    /// Diagnostics copied into the result.
    /// </param>
    /// <param name="cacheHit">
    /// Whether complete output was restored from cache.
    /// </param>
    /// <param name="cacheKey">
    /// Generation fingerprint when caching participated; otherwise null.
    /// </param>
    public BindingGenerationResult(
        TModule? module,
        bool success,
        IReadOnlyList<string> outputFiles,
        IReadOnlyList<BindingDiagnostic> diagnostics,
        bool cacheHit = false,
        string? cacheKey = null
    ) {
        ArgumentNullException.ThrowIfNull(outputFiles);
        ArgumentNullException.ThrowIfNull(diagnostics);
        this.module = module;
        this.success = success;
        this.outputFiles = Array.AsReadOnly(outputFiles.ToArray());
        this.diagnostics = Array.AsReadOnly(diagnostics.ToArray());
        this.cacheHit = cacheHit;
        this.cacheKey = cacheKey;
    }

    /// <summary>
    /// Gets the frozen analyzed module, or null when analysis could not produce one.
    /// </summary>
    public TModule? module { get; }
    /// <summary>
    /// Gets whether every required generation stage completed successfully.
    /// </summary>
    public bool success { get; }
    /// <summary>
    /// Gets an immutable copy of the files emitted or restored by this attempt.
    /// </summary>
    public IReadOnlyList<string> outputFiles { get; }
    /// <summary>
    /// Gets immutable frontend, analysis, and publication diagnostics for this attempt.
    /// </summary>
    public IReadOnlyList<BindingDiagnostic> diagnostics { get; }
    /// <summary>Gets whether the complete output was restored from the content-addressed cache.</summary>
    public bool cacheHit { get; }
    /// <summary>Gets the content-addressed cache key when incremental caching participated in the run.</summary>
    public string? cacheKey { get; }
}
