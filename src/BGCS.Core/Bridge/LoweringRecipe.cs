using System.Collections.Immutable;
using BGCS.Core.Managed;

namespace BGCS.Core.Bridge;

/// <summary>Declares a neutral fixed C representation, explicit native conversions and optional typed release resolution.</summary>
public sealed record TypeLoweringRecipe
{
    /// <summary>Gets the unique extension identity.</summary>
    public string id { get; init; } = string.Empty;
    /// <summary>Gets the source type glob; spaces are ignored and '*' matches any sequence.</summary>
    public string typePattern { get; init; } = string.Empty;
    /// <summary>Gets the matching priority, with the same conflict rules as plugin lowerings.</summary>
    public int priority { get; init; } = 100;
    /// <summary>Gets a fixed-width C scalar or pointer carrier spelling.</summary>
    public string cAbiType { get; init; } = "int32_t";
    /// <summary>Gets explicit named C inputs for one native parameter; an empty array uses the primary carrier.</summary>
    public ImmutableArray<RecipeAbiParameter> abiParameters { get; init; } = [];
    /// <summary>Gets the optional native input expression using value and nativeType placeholders.</summary>
    public string? parameterExpression { get; init; }
    /// <summary>Gets the optional C output expression using value and resultType placeholders.</summary>
    public string? resultExpression { get; init; }
    /// <summary>Gets declared native helper fragments used by this conversion.</summary>
    public ImmutableArray<ControlledSourceFragment> sourceFragments { get; init; } = [];
    /// <summary>Gets explicit managed conversion assertions; raw native carriers remain unchanged.</summary>
    public ManagedRecipeProjection? managedProjection { get; init; }
    /// <summary>Gets explicit owned-pointer cleanup resolved to a real native release declaration.</summary>
    public CppResultReleaseRequest? ownedResultRelease { get; init; }
}

/// <summary>Declares an explicit wrapper around an independently planned native call.</summary>
public sealed record CallableLoweringRecipe
{
    /// <summary>Gets the unique recipe identity.</summary>
    public string id { get; init; } = string.Empty;
    /// <summary>Gets the qualified or unqualified native callable glob.</summary>
    public string functionPattern { get; init; } = string.Empty;
    /// <summary>Gets the matching priority, with deterministic identity ordering.</summary>
    public int priority { get; init; } = 100;
    /// <summary>Gets an optional allocated C export name.</summary>
    public string? exportName { get; init; }
    /// <summary>Gets whether the matched declaration is intentionally excluded.</summary>
    public bool exclude { get; init; }
    /// <summary>Gets an optional native wrapper expression containing exactly one invocation placeholder.</summary>
    public string? invocationExpression { get; init; }
    /// <summary>Gets declared native helper fragments used by the wrapper.</summary>
    public ImmutableArray<ControlledSourceFragment> sourceFragments { get; init; } = [];
}
