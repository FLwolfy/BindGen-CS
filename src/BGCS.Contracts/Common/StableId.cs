using System;
using System.Text.Json.Serialization;

namespace BGCS.Contracts.Common;

/// <summary>Identifies a native fact independently of parser object identity.</summary>
public readonly record struct StableId
{
    /// <summary>Creates a nonempty fact identity.</summary>
    /// <param name="value">The deterministic declaration or type identity.</param>
    /// <exception cref="ArgumentException">The identity is empty.</exception>
    [JsonConstructor]
    public StableId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        this.value = value;
    }

    /// <summary>Gets the deterministic identity.</summary>
    public string value { get; }

    /// <inheritdoc/>
    public override string ToString() => value ?? string.Empty;
}
