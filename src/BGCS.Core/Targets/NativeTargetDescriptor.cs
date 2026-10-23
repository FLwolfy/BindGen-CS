using System;
using System.Collections.Immutable;
using BGCS.Core.Inputs;
using BGCS.Core.Native;

namespace BGCS.Core.Targets;

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
    /// The compiler target triple used to interpret source declarations during parsing.
    /// </param>
    /// <param name = "toolchain">
    /// The immutable SDK and compiler inputs for the resolved target.
    /// </param>
    /// <param name="capabilities">Optional immutable mechanisms declared by a custom target provider; these do not certify runtime support.</param>
    /// <param name="byteOrder">The target provider's explicit ABI byte-order fact, or Unknown when it cannot establish one.</param>
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
        NativeToolchainDescriptor toolchain,
        TargetCapabilities? capabilities = null,
        NativeByteOrder byteOrder = NativeByteOrder.Unknown
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId.value);
        if (targetId.value == "host")
            throw new ArgumentException("A resolved target cannot use the host alias.", nameof(targetId));
        ArgumentException.ThrowIfNullOrWhiteSpace(platformId);
        ArgumentException.ThrowIfNullOrWhiteSpace(architectureId);
        ArgumentException.ThrowIfNullOrWhiteSpace(abiId);
        ArgumentException.ThrowIfNullOrWhiteSpace(triple);
        ArgumentNullException.ThrowIfNull(toolchain);
        if (!Enum.IsDefined(byteOrder)) throw new ArgumentOutOfRangeException(nameof(byteOrder));
        this.targetId = targetId;
        this.platformId = platformId;
        this.architectureId = architectureId;
        this.abiId = abiId;
        this.triple = triple;
        this.toolchain = toolchain;
        this.capabilities = capabilities;
        this.byteOrder = byteOrder;
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
    /// <summary>Gets optional provider-declared parsing and native module mechanisms without a verification-stage claim.</summary>
    public TargetCapabilities? capabilities { get; }
    /// <summary>Gets the explicitly resolved native byte order independently of pointer width and generator host.</summary>
    public NativeByteOrder byteOrder { get; }

    /// <summary>Gets ordered provider-established parsing search roots, including explicit framework-directory semantics.</summary>
    public ImmutableArray<HeaderSearchPath> headerSearchPaths { get; init; } = [];
    /// <summary>Gets explicitly supplied native declaration spellings, or null when this provider does not supply native bridge emission capability.</summary>
    public NativeSourceConventions? sourceConventions { get; init; }
}
