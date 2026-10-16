using System.Collections.Immutable;

namespace BGCS.Contracts.Managed;

/// <summary>Selects deterministic word casing independently of native identities and ABI storage.</summary>
public enum ManagedNameCasing
{
    /// <summary>Preserves source spelling.</summary>
    Preserve,
    /// <summary>Capitalizes each word.</summary>
    Pascal,
    /// <summary>Starts with a lowercase word followed by capitalized words.</summary>
    Camel,
    /// <summary>Joins lowercase words with underscores.</summary>
    Snake,
    /// <summary>Joins uppercase words with underscores.</summary>
    ScreamingSnake,
    /// <summary>Joins uppercase words without separators.</summary>
    UpperFlat,
    /// <summary>Joins lowercase words without separators.</summary>
    LowerFlat
}

/// <summary>Selects an immutable vocabulary for explicitly cased, all-uppercase flat identifiers.</summary>
public enum ManagedFlatIdentifierSegmentation
{
    /// <summary>Uses only ordinary case and separator boundaries.</summary>
    None,
    /// <summary>Uses the product's fixed, versioned English vocabulary.</summary>
    English,
    /// <summary>Uses only the explicitly supplied immutable word list.</summary>
    Custom
}

/// <summary>Applies exact affix removal, invariant word casing and explicit output affixes.</summary>
public sealed record ManagedNameRule
{
    /// <summary>Gets the word casing, or null to retain the category's ordinary default.</summary>
    public ManagedNameCasing? casing { get; init; }
    /// <summary>Gets exact source prefixes; the longest matching prefix is removed once.</summary>
    public ImmutableArray<string> removePrefixes { get; init; } = [];
    /// <summary>Gets exact source suffixes; the longest matching suffix is removed once.</summary>
    public ImmutableArray<string> removeSuffixes { get; init; } = [];
    /// <summary>Gets the explicit managed prefix after word conversion.</summary>
    public string prefix { get; init; } = string.Empty;
    /// <summary>Gets the explicit managed suffix after word conversion.</summary>
    public string suffix { get; init; } = string.Empty;
}

/// <summary>Replaces one literal substring once per occurrence, using an explicit ordinal comparison.</summary>
/// <param name="match">The nonempty substring to replace.</param>
/// <param name="replacement">The literal replacement, which may be empty.</param>
public sealed record ManagedNameReplacement(
    string match,
    string replacement
)
{
    /// <summary>Gets whether matching ignores case using ordinal Unicode rules.</summary>
    public bool ignoreCase { get; init; }
}

/// <summary>Groups presentation policies; exact symbol mappings always take precedence.</summary>
public sealed record ManagedNamingPolicy
{
    /// <summary>Gets optional longest-prefix segmentation of all-uppercase flat words; exact mappings and preserved casing bypass it.</summary>
    public ManagedFlatIdentifierSegmentation flatIdentifierSegmentation { get; init; }
    /// <summary>Gets at most 4096 distinct case-insensitive letter-only words, each at most 128 characters; valid only with Custom segmentation.</summary>
    public ImmutableArray<string> flatIdentifierWords { get; init; } = [];
    /// <summary>Gets ordered literal substring replacements after category naming and before reserved-name protection; exact symbol mappings bypass this policy.</summary>
    public ImmutableArray<ManagedNameReplacement> replacements { get; init; } = [];
    /// <summary>Gets native record naming.</summary>
    public ManagedNameRule types { get; init; } = new();
    /// <summary>Gets native enumeration type naming.</summary>
    public ManagedNameRule enumerations { get; init; } = new();
    /// <summary>Gets enumeration value naming.</summary>
    public ManagedNameRule enumerators { get; init; } = new();
    /// <summary>Gets callable naming.</summary>
    public ManagedNameRule functions { get; init; } = new();
    /// <summary>Gets callback type naming.</summary>
    public ManagedNameRule callbacks { get; init; } = new();
    /// <summary>Gets field and bitfield naming.</summary>
    public ManagedNameRule fields { get; init; } = new();
    /// <summary>Gets callable input naming.</summary>
    public ManagedNameRule parameters { get; init; } = new();
    /// <summary>Gets scalar and string constant naming.</summary>
    public ManagedNameRule constants { get; init; } = new();
    /// <summary>Gets native source function alias naming.</summary>
    public ManagedNameRule aliases { get; init; } = new();
    /// <summary>Gets exact enumeration names mapped to optional value-prefix rules.</summary>
    public ImmutableDictionary<string, ManagedNameRule> enumerationValues { get; init; } = ImmutableDictionary<string, ManagedNameRule>.Empty;
    /// <summary>Gets additional reserved managed names that receive the reserved suffix.</summary>
    public ImmutableArray<string> reservedNames { get; init; } = [];
    /// <summary>Gets the suffix for additional reserved names; C# language keywords are escaped by the backend.</summary>
    public string reservedSuffix { get; init; } = "_";
}
