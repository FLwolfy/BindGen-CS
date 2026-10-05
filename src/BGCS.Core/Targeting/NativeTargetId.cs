using System;

namespace BGCS.Core.Targeting;

/// <summary>
/// Identifies a native platform, architecture and ABI independently of the generator host.
/// </summary>
public readonly record struct NativeTargetId
{
    /// <summary>
    /// Creates a stable target identifier for a registered native target provider.
    /// </summary>
    /// <param name = "value">
    /// A lowercase identifier containing letters, digits, dots or hyphens.
    /// The identifier <c>host</c> requests the current generator host target.
    /// </param>
    /// <exception cref = "ArgumentException">
    /// The identifier is empty or contains unsupported characters.
    /// </exception>
    public NativeTargetId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        foreach (char character in value)
        {
            if (character is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '-' and not '.')
                throw new ArgumentException("Native target IDs must use lowercase letters, digits, dots or hyphens.", nameof(value));
        }

        this.value = value;
    }

    /// <summary>
    /// Gets the stable identifier; a default struct has no identifier and cannot be resolved.
    /// </summary>
    public string value { get; }

    /// <inheritdoc/>
    public override string ToString() => value ?? string.Empty;
}
