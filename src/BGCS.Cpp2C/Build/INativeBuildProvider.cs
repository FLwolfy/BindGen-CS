using System;

namespace BGCS.Cpp2C.Build;

/// <summary>
/// Converts a validated bridge manifest into a shell-independent compiler invocation.
/// </summary>
public interface INativeBuildProvider
{
    /// <summary>
    /// Gets the stable provider identifier used in plans and diagnostics.
    /// </summary>
    string name { get; }

    /// <summary>
    /// Freezes compiler inputs without starting a process or writing build artifacts.
    /// </summary>
    /// <param name="manifest">Validated target, source and link inputs.</param>
    /// <param name="manifestPath">Manifest path used to resolve relative input paths.</param>
    /// <param name="outputPath">Optional explicit artifact path; null uses the manifest's default layout.</param>
    /// <returns>A frozen execution description whose process arguments require no shell parsing.</returns>
    /// <exception cref="ArgumentNullException">The manifest is null.</exception>
    /// <exception cref="ArgumentException">The manifest path is empty.</exception>
    /// <exception cref="NotSupportedException">The provider cannot build the requested target.</exception>
    NativeBuildPlan CreatePlan(
        CppBridgeBuildManifest manifest,
        string manifestPath,
        string? outputPath = null
    );
}
