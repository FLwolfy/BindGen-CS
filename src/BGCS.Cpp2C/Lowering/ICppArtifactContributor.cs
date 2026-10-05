using System.Collections.Generic;

namespace BGCS.Cpp2C.Lowering;

/// <summary>Contributes deterministic native or managed artifacts to the generated package.</summary>
public interface ICppArtifactContributor
{
    /// <summary>
    /// Stable registration name, unique within this extension category.
    /// </summary>
    string name { get; }

    /// <summary>
    /// Selection priority; higher values are tried first and equal priorities use ordinal name order.
    /// </summary>
    int priority { get; }

    /// <summary>
    /// Supplies deterministic artifacts for the requested generation stage.
    /// </summary>
    /// <param name="context">Current target, configuration and artifact stage.</param>
    /// <returns>A non-null sequence; an empty sequence contributes no artifacts.</returns>
    IReadOnlyList<CppGeneratedArtifact> Contribute(CppArtifactContext context);
}
