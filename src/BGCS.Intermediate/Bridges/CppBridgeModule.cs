using System;
using System.Collections.Generic;
using System.Linq;

namespace BGCS.Intermediate.Bridges;

/// <summary>
/// Owns one frozen, target-specific bridge analysis and its lowered source artifacts.
/// </summary>
public sealed class CppBridgeModule
{
    /// <summary>
    /// Copies the complete bridge analysis into an emitter-independent result.
    /// </summary>
    /// <param name = "targetId">
    /// The concrete native target identity used during parsing and lowering.
    /// </param>
    /// <param name = "types">
    /// The analyzed layout and type identities.
    /// </param>
    /// <param name = "functions">
    /// The callable ABI and ownership facts.
    /// </param>
    /// <param name = "artifacts">
    /// The lowered source operations, including extension artifacts.
    /// </param>
    /// <param name = "diagnostics">
    /// The diagnostics produced before emission.
    /// </param>
    /// <exception cref = "ArgumentNullException">
    /// A required input sequence is null.
    /// </exception>
    public CppBridgeModule(
        string targetId,
        IEnumerable<CppBridgeType> types,
        IEnumerable<CppBridgeFunction> functions,
        IEnumerable<CppBridgeArtifact> artifacts,
        IEnumerable<BindingDiagnostic> diagnostics
    ) {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentNullException.ThrowIfNull(artifacts);
        ArgumentNullException.ThrowIfNull(diagnostics);
        this.targetId = targetId;
        this.types = Array.AsReadOnly(types.ToArray());
        this.functions = Array.AsReadOnly(functions.ToArray());
        this.artifacts = Array.AsReadOnly(artifacts.ToArray());
        this.diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }

    /// <summary>
    /// Gets the concrete native target identity.
    /// </summary>
    public string targetId { get; }
    /// <summary>
    /// Gets the frozen type layout facts.
    /// </summary>
    public IReadOnlyList<CppBridgeType> types { get; }
    /// <summary>
    /// Gets the frozen callable ABI facts.
    /// </summary>
    public IReadOnlyList<CppBridgeFunction> functions { get; }
    /// <summary>
    /// Gets the frozen source operations for all native artifacts.
    /// </summary>
    public IReadOnlyList<CppBridgeArtifact> artifacts { get; }
    /// <summary>
    /// Gets the analysis diagnostics.
    /// </summary>
    public IReadOnlyList<BindingDiagnostic> diagnostics { get; }

    /// <summary>
    /// Gets frozen managed extension contributions, or null when managed bindings were not requested.
    /// </summary>
    public CppManagedArtifactPlan? managedArtifacts { get; init; }
}
