using System;

namespace BGCS.Core.Hosting;

/// <summary>Describes the observed comparison behavior of child names in one host directory.</summary>
/// <param name="caseSensitive">Whether differently cased child names are distinct.</param>
public sealed record HostPathSemantics(bool caseSensitive)
{
    /// <summary>Gets the name comparison to use within this directory only.</summary>
    public StringComparison comparison => caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
    /// <summary>Gets the name comparer to use within this directory only.</summary>
    public StringComparer comparer => caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
}
