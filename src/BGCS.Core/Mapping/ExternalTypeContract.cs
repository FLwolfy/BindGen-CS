namespace BGCS.Core.Mapping;

/// <summary>Controls whether an external managed carrier may cross the native ABI by value.</summary>
public enum ExternalTypeByValuePolicy
{
    /// <summary>Only pointer use is accepted; by-value use is rejected.</summary>
    Reject,

    /// <summary>By-value use is accepted only when the parsed native size and alignment match the declared carrier layout.</summary>
    RequireLayoutMatch,

    /// <summary>By-value use continues without a native layout match and emits an auditable safety diagnostic.</summary>
    BypassLayoutValidation
}

/// <summary>
/// Declares the ABI contract for a managed value type supplied by the consuming project instead of emitted by BindGen-CS.
/// </summary>
public sealed class ExternalTypeContract
{
    /// <summary>
    /// Native record or typedef selectors represented by the same managed carrier layout. Selectors use ordinal
    /// matching and may contain <c>*</c> for any sequence or <c>?</c> for one character.
    /// </summary>
    public List<string> NativeTypes { get; set; } = [];

    /// <summary>
    /// Managed type selectors produced by the corresponding <c>TypeMappings</c> entries. Selectors use ordinal
    /// matching and may contain <c>*</c> for any sequence or <c>?</c> for one character.
    /// </summary>
    public List<string> ManagedTypes { get; set; } = [];

    /// <summary>Expected managed carrier size in bytes for the configured target.</summary>
    public int Size { get; set; }

    /// <summary>Expected managed carrier alignment in bytes for the configured target.</summary>
    public int Alignment { get; set; }

    /// <summary>Policy applied when the external carrier appears in an ABI by-value position.</summary>
    public ExternalTypeByValuePolicy ByValuePolicy { get; set; } = ExternalTypeByValuePolicy.Reject;

    /// <summary>Returns whether this contract selects the supplied native-to-managed mapping.</summary>
    public bool Matches(string nativeType, string managedType)
    {
        ArgumentNullException.ThrowIfNull(nativeType);
        ArgumentNullException.ThrowIfNull(managedType);
        return NativeTypes.Any(selector => MatchesSelector(selector, nativeType)) &&
            ManagedTypes.Any(selector => MatchesSelector(selector, managedType));
    }

    /// <summary>Matches a type name against an ordinal selector containing optional <c>*</c> and <c>?</c> wildcards.</summary>
    public static bool MatchesSelector(string selector, string value)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(value);
        int selectorIndex = 0;
        int valueIndex = 0;
        int starIndex = -1;
        int starValueIndex = -1;
        while (valueIndex < value.Length)
        {
            if (selectorIndex < selector.Length &&
                (selector[selectorIndex] == '?' || selector[selectorIndex] == value[valueIndex]))
            {
                selectorIndex++;
                valueIndex++;
                continue;
            }
            if (selectorIndex < selector.Length && selector[selectorIndex] == '*')
            {
                starIndex = selectorIndex++;
                starValueIndex = valueIndex;
                continue;
            }
            if (starIndex < 0)
                return false;
            selectorIndex = starIndex + 1;
            valueIndex = ++starValueIndex;
        }
        while (selectorIndex < selector.Length && selector[selectorIndex] == '*')
            selectorIndex++;
        return selectorIndex == selector.Length;
    }
}
