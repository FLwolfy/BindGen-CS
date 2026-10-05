using System;
using System.Collections.Generic;

namespace BGCS.Configuration.Mapping;

/// <summary>
/// Captures the immutable name segments removed from native enumeration items.
/// </summary>
public readonly struct EnumPrefix
{
    private readonly IReadOnlyList<string>? m_parts;

    /// <summary>
    /// Copies the prefix segments so later caller mutations cannot change name generation.
    /// </summary>
    /// <param name="prefixes">Ordered, non-null name segments.</param>
    /// <exception cref="ArgumentNullException">A sequence or one of its segments is null.</exception>
    public EnumPrefix(string[] prefixes)
    {
        ArgumentNullException.ThrowIfNull(prefixes);
        string[] copy = (string[])prefixes.Clone();
        foreach (string segment in copy)
            ArgumentNullException.ThrowIfNull(segment, nameof(prefixes));
        m_parts = Array.AsReadOnly(copy);
    }

    /// <summary>
    /// Gets the captured segments, or an empty sequence for the default prefix.
    /// </summary>
    public IReadOnlyList<string> parts => m_parts ?? Array.Empty<string>();
}
