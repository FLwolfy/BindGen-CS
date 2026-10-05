using BGCS.Cpp2C.Configuration;
namespace BGCS.Cpp2C.Lowering;

/// <summary>Context supplied when a plugin contributes bridge or managed artifacts.</summary>
/// <param name="configuration">
/// Configuration owned by the current generation.
/// </param>
/// <param name="targetIdentifier">
/// Resolved native target identifier.
/// </param>
/// <param name="stage">
/// Artifact category requested by the generation stage.
/// </param>
public sealed record CppArtifactContext(
    Cpp2CGeneratorConfig configuration,
    string targetIdentifier,
    CppGeneratedArtifactKind stage
);
