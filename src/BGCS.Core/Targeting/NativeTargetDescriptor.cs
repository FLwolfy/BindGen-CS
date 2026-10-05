using System;

namespace BGCS.Core.Targeting;

/// <summary>
/// Describes the resolved native ABI without retaining parser objects or discovery callbacks.
/// </summary>
public sealed class NativeTargetDescriptor
{
    /// <summary>
    /// Creates a concrete target resolved by a native target provider.
    /// </summary>
    /// <param name = "targetId">
    /// The concrete stable target ID; host aliases must already be resolved.
    /// </param>
    /// <param name = "platformId">
    /// The provider's open platform family ID.
    /// </param>
    /// <param name = "architectureId">
    /// The target processor architecture ID.
    /// </param>
    /// <param name = "abiId">
    /// The provider's native ABI ID.
    /// </param>
    /// <param name = "triple">
    /// The compiler target triple for parsing and native compilation.
    /// </param>
    /// <param name = "toolchain">
    /// The immutable SDK and compiler inputs for the resolved target.
    /// </param>
    /// <exception cref = "ArgumentException">
    /// An identity or triple is empty, or the target still uses a host alias.
    /// </exception>
    /// <exception cref = "ArgumentNullException">
    /// The toolchain descriptor is null.
    /// </exception>
    public NativeTargetDescriptor(
        NativeTargetId targetId,
        string platformId,
        string architectureId,
        string abiId,
        string triple,
        NativeToolchainDescriptor toolchain
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId.value);
        if (targetId.value == "host")
            throw new ArgumentException("A resolved target cannot use the host alias.", nameof(targetId));
        ArgumentException.ThrowIfNullOrWhiteSpace(platformId);
        ArgumentException.ThrowIfNullOrWhiteSpace(architectureId);
        ArgumentException.ThrowIfNullOrWhiteSpace(abiId);
        ArgumentException.ThrowIfNullOrWhiteSpace(triple);
        ArgumentNullException.ThrowIfNull(toolchain);
        this.targetId = targetId;
        this.platformId = platformId;
        this.architectureId = architectureId;
        this.abiId = abiId;
        this.triple = triple;
        this.toolchain = toolchain;
    }

    /// <summary>
    /// Gets the concrete stable target ID.
    /// </summary>
    public NativeTargetId targetId { get; }
    /// <summary>
    /// Gets the open platform family ID.
    /// </summary>
    public string platformId { get; }
    /// <summary>
    /// Gets the target processor architecture ID.
    /// </summary>
    public string architectureId { get; }
    /// <summary>
    /// Gets the native ABI ID.
    /// </summary>
    public string abiId { get; }
    /// <summary>
    /// Gets the target compiler triple.
    /// </summary>
    public string triple { get; }
    /// <summary>
    /// Gets the resolved toolchain inputs.
    /// </summary>
    public NativeToolchainDescriptor toolchain { get; }
}
