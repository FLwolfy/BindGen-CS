using System;
using System.Collections.Generic;
using System.Linq;

namespace BGCS.Intermediate.Bridges;

/// <summary>
/// Owns AST-independent managed extension sources and conversion declarations for one bridge generation.
/// </summary>
public sealed class CppManagedArtifactPlan
{
    /// <summary>
    /// Copies the complete managed contribution into read-only snapshots.
    /// </summary>
    /// <param name="targetId">Concrete target identity shared with the C binding module.</param>
    /// <param name="namespaceName">Namespace receiving managed conversions.</param>
    /// <param name="apiName">Managed API name used to group conversions.</param>
    /// <param name="sources">Source contributions produced by lowering extensions.</param>
    /// <param name="projections">Conversions whose carriers are identified by generated native typedefs.</param>
    /// <exception cref="ArgumentNullException">A required source or projection sequence is null.</exception>
    public CppManagedArtifactPlan(
        string targetId,
        string namespaceName,
        string apiName,
        IEnumerable<CppManagedSourceArtifact> sources,
        IEnumerable<CppManagedProjectionBinding> projections
    ) {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(projections);
        this.targetId = targetId;
        this.namespaceName = namespaceName;
        this.apiName = apiName;
        this.sources = Array.AsReadOnly(sources.ToArray());
        this.projections = Array.AsReadOnly(projections.ToArray());
    }

    /// <summary>Gets the concrete native target identity.</summary>
    public string targetId { get; }
    /// <summary>Gets the namespace receiving managed conversions.</summary>
    public string namespaceName { get; }
    /// <summary>Gets the managed API name grouping conversions.</summary>
    public string apiName { get; }
    /// <summary>Gets the frozen extension source contributions.</summary>
    public IReadOnlyList<CppManagedSourceArtifact> sources { get; }
    /// <summary>Gets the frozen conversions whose carriers must be resolved from C binding IR.</summary>
    public IReadOnlyList<CppManagedProjectionBinding> projections { get; }
}
