using System;

namespace BGCS.Contracts.Targets;

/// <summary>
/// Freezes target selection and caller-supplied toolchain inputs for native generation.
/// </summary>
public sealed class NativeTargetRequest
{
    /// <summary>
    /// Creates a request without probing the host or choosing a compiler.
    /// </summary>
    /// <param name = "targetId">
    /// The stable ID understood by a registered provider, or <c>host</c>.
    /// </param>
    /// <param name = "toolchain">
    /// Explicit SDK locations, header paths and parser arguments; null requests provider defaults.
    /// </param>
    /// <param name = "tripleOverride">
    /// An explicit compiler triple, or null to use the provider's triple.
    /// </param>
    /// <exception cref = "ArgumentException">
    /// The target ID is uninitialized or the triple override is whitespace.
    /// </exception>
    public NativeTargetRequest(
        NativeTargetId targetId,
        NativeToolchainDescriptor? toolchain = null,
        string? tripleOverride = null
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId.value);
        if (tripleOverride is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tripleOverride);
            if (tripleOverride.Length > 256 || tripleOverride.AsSpan().IndexOfAnyExcept("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_.-".AsSpan()) >= 0)
                throw new ArgumentException("An explicit native compiler triple must be a bounded portable triple token.", nameof(tripleOverride));
        }
        this.targetId = targetId;
        this.toolchain = toolchain ?? new();
        this.tripleOverride = tripleOverride;
    }

    /// <summary>
    /// Gets the requested target identity.
    /// </summary>
    public NativeTargetId targetId { get; }
    /// <summary>
    /// Gets the explicit toolchain inputs, retaining no discovery services.
    /// </summary>
    public NativeToolchainDescriptor toolchain { get; }
    /// <summary>
    /// Gets the optional explicit compiler triple.
    /// </summary>
    public string? tripleOverride { get; }
}
