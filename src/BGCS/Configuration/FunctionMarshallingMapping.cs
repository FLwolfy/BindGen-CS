using System;
using System.Collections.Generic;

namespace BGCS.Configuration;

/// <summary>
/// Overrides return and parameter marshalling for one native function.
/// </summary>
public sealed class FunctionMarshallingMapping
{
    /// <summary>Gets or sets the return-value marshalling override.</summary>
    public MarshallingMapping? @return { get; set; }
    /// <summary>Gets parameter overrides keyed by exported native parameter name.</summary>
    public Dictionary<string, MarshallingMapping> parameters { get; set; } = new(StringComparer.Ordinal);
}
