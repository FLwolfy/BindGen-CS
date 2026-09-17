namespace BGCS;

using BGCS.Intermediate;

/// <summary>
/// Overrides inferred marshalling semantics for one native value.
/// </summary>
public sealed class MarshallingMapping
{
    /// <summary>Gets or sets the marshalling strategy override.</summary>
    public MarshallingStrategy? Strategy { get; set; }

    /// <summary>Gets or sets the native ownership override.</summary>
    public BindingOwnership? Ownership { get; set; }

    /// <summary>Gets or sets the native string encoding override.</summary>
    public BindingStringEncoding? Encoding { get; set; }

    /// <summary>Gets or sets the related native length parameter name.</summary>
    public string? LengthParameter { get; set; }

    /// <summary>Gets or sets the related native capacity parameter name.</summary>
    public string? CapacityParameter { get; set; }

    /// <summary>Gets or sets the related native written-count parameter name.</summary>
    public string? WrittenCountParameter { get; set; }

    /// <summary>Gets or sets the native cleanup function used for owned values.</summary>
    public string? CleanupFunction { get; set; }

    /// <summary>Gets or sets whether generated marshalling must perform cleanup.</summary>
    public bool? RequiresCleanup { get; set; }

    /// <summary>Gets or sets whether a string or sequence uses a terminating zero element.</summary>
    public bool? NullTerminated { get; set; }
}

/// <summary>
/// Overrides return and parameter marshalling for one native function.
/// </summary>
public sealed class FunctionMarshallingMapping
{
    /// <summary>Gets or sets the return-value marshalling override.</summary>
    public MarshallingMapping? Return { get; set; }

    /// <summary>Gets parameter overrides keyed by exported native parameter name.</summary>
    public Dictionary<string, MarshallingMapping> Parameters { get; set; } = new(StringComparer.Ordinal);
}
