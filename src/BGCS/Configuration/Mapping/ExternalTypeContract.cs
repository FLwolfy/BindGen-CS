using System;
using System.Collections.Generic;
using System.Linq;

namespace BGCS.Configuration.Mapping;

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
    public List<string> nativeTypes { get; set; } = [];
    /// <summary>
    /// Managed type selectors produced by the corresponding <c>TypeMappings</c> entries. Selectors use ordinal
    /// matching and may contain <c>*</c> for any sequence or <c>?</c> for one character.
    /// </summary>
    public List<string> managedTypes { get; set; } = [];
    /// <summary>Expected managed carrier size in bytes for the configured target.</summary>
    public int size { get; set; }
    /// <summary>Expected managed carrier alignment in bytes for the configured target.</summary>
    public int alignment { get; set; }
    /// <summary>Policy applied when the external carrier appears in an ABI by-value position.</summary>
    public ExternalTypeByValuePolicy byValuePolicy { get; set; } = ExternalTypeByValuePolicy.Reject;

    /// <summary>
    /// Checks whether both native and managed names match a selector in this layout contract.
    /// </summary>
    /// <param name="nativeType">
    /// The exact native record or typedef name to select.
    /// </param>
    /// <param name="managedType">
    /// The mapped managed carrier name to select.
    /// </param>
    /// <returns>
    /// True only when both name groups accept the mapping; empty selector groups do not match.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Either type name is null.
    /// </exception>
    public bool Matches(
        string nativeType,
        string managedType
    ) {
        ArgumentNullException.ThrowIfNull(nativeType);
        ArgumentNullException.ThrowIfNull(managedType);
        return this.nativeTypes.Any(selector => MatchesSelector(selector, nativeType)) && this.managedTypes.Any(selector => MatchesSelector(selector, managedType));
    }

    /// <summary>
    /// Matches the entire name against an ordinal selector with star and single-character wildcards.
    /// </summary>
    /// <param name="selector">
    /// The case-sensitive selector; star matches any sequence and question mark matches one character.
    /// </param>
    /// <param name="value">
    /// The complete name to match.
    /// </param>
    /// <returns>
    /// True when the selector consumes the entire name, including empty names accepted by an empty selector or star.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// The selector or name is null.
    /// </exception>
    public static bool MatchesSelector(
        string selector,
        string value
    ) {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(value);
        int selectorIndex = 0;
        int valueIndex = 0;
        int starIndex = -1;
        int starValueIndex = -1;
        while (valueIndex < value.Length)
        {
            if (selectorIndex < selector.Length && (selector[selectorIndex] == '?' || selector[selectorIndex] == value[valueIndex]))
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
