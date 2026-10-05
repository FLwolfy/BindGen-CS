using System.Diagnostics.CodeAnalysis;

namespace BGCS.Core.Targeting;

/// <summary>
/// Resolves native targets belonging to one SDK or platform family.
/// </summary>
public interface INativeTargetProvider
{
    /// <summary>
    /// Resolves a supported target without claiming targets owned by other providers.
    /// </summary>
    /// <param name = "request">
    /// The concrete target identity and explicit toolchain inputs.
    /// </param>
    /// <param name = "target">
    /// Receives the resolved target on success, or null when this provider does not support it.
    /// </param>
    /// <returns>
    /// True when the provider recognizes and resolves the request; false when it does not.
    /// </returns>
    bool TryResolve(
        NativeTargetRequest request,
        [NotNullWhen(true)] out NativeTargetDescriptor? target
    );
}
