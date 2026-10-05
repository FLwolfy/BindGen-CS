using System.Collections.Generic;

namespace BGCS.Cpp2C.Configuration;

/// <summary>
/// Identifies a base configuration and the inherited values excluded from composition.
/// </summary>
public sealed class BaseConfig
{
    /// <summary>
    /// Gets or sets a file://, http:// or https:// reference resolved from the containing document.
    /// </summary>
    public string? url { get; set; }

    /// <summary>
    /// Gets or sets the JSON property paths removed from the base before child values are applied.
    /// </summary>
    public HashSet<string> ignoredProperties { get; set; } = [];
}
