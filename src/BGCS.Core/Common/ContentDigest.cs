using System;
using System.Text.Json.Serialization;

namespace BGCS.Core.Common;

/// <summary>Identifies exact content using a normalized SHA-256 digest.</summary>
public readonly record struct ContentDigest
{
    /// <summary>Validates a serialized or freshly calculated digest.</summary>
    /// <param name="value">Exactly 64 lowercase hexadecimal characters.</param>
    /// <exception cref="ArgumentException">The value is not a normalized SHA-256 digest.</exception>
    [JsonConstructor]
    public ContentDigest(string value)
    {
        if (value is null || value.Length != 64)
            throw new ArgumentException("A content digest must contain 64 lowercase hexadecimal characters.", nameof(value));
        foreach (char character in value)
            if (character is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
                throw new ArgumentException("A content digest must contain 64 lowercase hexadecimal characters.", nameof(value));
        this.value = value;
    }

    /// <summary>Gets the normalized digest.</summary>
    public string value { get; }
}
