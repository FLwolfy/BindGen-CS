using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BGCS.Contracts.Managed;

namespace BGCS.CSharp.Projection;

/// <summary>Converts source words without changing native symbol identity or storing mutable name caches.</summary>
public static class ManagedNamingResolver
{
    /// <summary>Validates a complete immutable naming policy before native parsing begins.</summary>
    /// <param name="policy">The category rules and reserved names.</param>
    public static void Validate(ManagedNamingPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(policy.enumerationValues);
        if (!Enum.IsDefined(policy.flatIdentifierSegmentation)) throw new ArgumentException("The flat identifier segmentation mode is undefined.");
        if (policy.flatIdentifierWords.IsDefault) throw new ArgumentException("Flat identifier words must be initialized.");
        if (policy.flatIdentifierSegmentation != ManagedFlatIdentifierSegmentation.Custom && !policy.flatIdentifierWords.IsEmpty)
            throw new ArgumentException("Flat identifier words require Custom segmentation.");
        if (policy.flatIdentifierSegmentation == ManagedFlatIdentifierSegmentation.Custom)
        {
            if (policy.flatIdentifierWords.IsEmpty || policy.flatIdentifierWords.Length > 4096)
                throw new ArgumentException("Custom segmentation requires between 1 and 4096 words.");
            HashSet<string> words = new(StringComparer.OrdinalIgnoreCase);
            foreach (string word in policy.flatIdentifierWords)
                if (string.IsNullOrEmpty(word) || word.Length > 128 || word.Any(character => !char.IsLetter(character)) || !words.Add(word))
                    throw new ArgumentException("Custom words must be distinct case-insensitive letter-only words of at most 128 characters.");
        }
        if (policy.replacements.IsDefault) throw new ArgumentException("Naming replacements must be initialized.");
        foreach (ManagedNameReplacement replacement in policy.replacements)
        {
            ArgumentNullException.ThrowIfNull(replacement);
            ArgumentException.ThrowIfNullOrEmpty(replacement.match);
            ArgumentNullException.ThrowIfNull(replacement.replacement);
        }
        if (policy.reservedNames.IsDefault) throw new ArgumentException("Reserved names must be initialized.");
        ArgumentNullException.ThrowIfNull(policy.reservedSuffix);
        if (!policy.reservedNames.IsEmpty && policy.reservedSuffix.Length == 0) throw new ArgumentException("Reserved names require a nonempty replacement suffix.");
        foreach (string name in policy.reservedNames) ArgumentException.ThrowIfNullOrWhiteSpace(name);
        foreach (ManagedNameRule rule in new[] { policy.types, policy.enumerations, policy.enumerators, policy.functions,
            policy.callbacks, policy.fields, policy.parameters, policy.constants, policy.aliases }.Concat(policy.enumerationValues.Values))
        {
            ArgumentNullException.ThrowIfNull(rule);
            if (rule.casing is { } casing && !Enum.IsDefined(casing)) throw new ArgumentException("A managed naming case is undefined.");
            if (rule.removePrefixes.IsDefault || rule.removeSuffixes.IsDefault) throw new ArgumentException("Naming affix lists must be initialized.");
            foreach (string affix in rule.removePrefixes.Concat(rule.removeSuffixes)) ArgumentException.ThrowIfNullOrWhiteSpace(affix);
            ArgumentNullException.ThrowIfNull(rule.prefix);
            ArgumentNullException.ThrowIfNull(rule.suffix);
        }
        foreach (string name in policy.enumerationValues.Keys) ArgumentException.ThrowIfNullOrWhiteSpace(name);
    }

    internal static string Apply(
        string source,
        string ordinary,
        ManagedNameRule rule,
        ManagedNamingPolicy policy,
        bool preserveSourceByDefault = false,
        FlatIdentifierWordIndex? wordIndex = null
    ) {
        string stem = source;
        string? prefix = rule.removePrefixes.OrderByDescending(value => value.Length).ThenBy(value => value, StringComparer.Ordinal)
            .FirstOrDefault(value => stem.StartsWith(value, StringComparison.Ordinal));
        if (prefix is not null) stem = stem[prefix.Length..];
        string? suffix = rule.removeSuffixes.OrderByDescending(value => value.Length).ThenBy(value => value, StringComparer.Ordinal)
            .FirstOrDefault(value => stem.EndsWith(value, StringComparison.Ordinal));
        if (suffix is not null) stem = stem[..^suffix.Length];
        if (stem.Length == 0) throw new ArgumentException("Naming affix removal consumed the entire source identifier: " + source);
        bool edited = prefix is not null || suffix is not null;
        string result = rule.casing is { } casing ? ConvertCore(stem, casing, wordIndex)
            : edited ? preserveSourceByDefault ? stem : ManagedNameResolver.Identifier(stem, stem) : ordinary;
        result = rule.prefix + result + rule.suffix;
        foreach (ManagedNameReplacement replacement in policy.replacements)
            result = result.Replace(replacement.match, replacement.replacement,
                replacement.ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        if (result.Length == 0) throw new ArgumentException("Naming replacements consumed the entire managed identifier: " + source);
        if (policy.reservedNames.Contains(result, StringComparer.Ordinal)) result += policy.reservedSuffix;
        return result;
    }

    /// <summary>Splits separators, case transitions and acronym boundaries using invariant Unicode character casing.</summary>
    /// <param name="source">The identifier spelling.</param>
    /// <param name="casing">The selected word policy.</param>
    /// <returns>A deterministic managed spelling before backend keyword escaping.</returns>
    public static string Convert(
        string source,
        ManagedNameCasing casing
    ) => ConvertCore(source, casing, null);

    /// <summary>Converts identifier words using one validated immutable segmentation policy; source spelling and native identity remain unchanged.</summary>
    /// <param name="source">The identifier spelling.</param>
    /// <param name="casing">The selected output casing.</param>
    /// <param name="policy">The explicit vocabulary selection.</param>
    /// <returns>The deterministic converted identifier.</returns>
    public static string Convert(
        string source,
        ManagedNameCasing casing,
        ManagedNamingPolicy policy
    ) {
        Validate(policy);
        return ConvertCore(source, casing, FlatIdentifierWordIndex.Create(policy));
    }

    private static string ConvertCore(
        string source,
        ManagedNameCasing casing,
        FlatIdentifierWordIndex? wordIndex
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (!Enum.IsDefined(casing)) throw new ArgumentException("A managed naming case is undefined.", nameof(casing));
        if (casing == ManagedNameCasing.Preserve) return source;
        List<string> words = [];
        int start = -1;
        for (int index = 0; index < source.Length; index++)
        {
            char current = source[index];
            if (!char.IsLetterOrDigit(current))
            {
                if (start >= 0) words.Add(source[start..index]);
                start = -1;
                continue;
            }
            if (start < 0) { start = index; continue; }
            bool boundary = char.IsUpper(current) && (char.IsLower(source[index - 1])
                || char.IsDigit(source[index - 1]) || char.IsUpper(source[index - 1]) && index + 1 < source.Length && char.IsLower(source[index + 1]));
            if (boundary) { words.Add(source[start..index]); start = index; }
        }
        if (start >= 0) words.Add(source[start..]);
        if (words.Count == 0) throw new ArgumentException("A managed identifier must contain a letter or digit.", nameof(source));
        if (wordIndex is not null && !source.Any(char.IsLower))
        {
            List<string> segmented = [];
            foreach (string word in words) wordIndex.AppendWords(word, segmented);
            words = segmented;
        }
        string Capitalize(string value) => char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
        string value = casing switch
        {
            ManagedNameCasing.Pascal => string.Concat(words.Select(Capitalize)),
            ManagedNameCasing.Camel => words[0].ToLowerInvariant() + string.Concat(words.Skip(1).Select(Capitalize)),
            ManagedNameCasing.Snake => string.Join('_', words.Select(word => word.ToLowerInvariant())),
            ManagedNameCasing.ScreamingSnake => string.Join('_', words.Select(word => word.ToUpperInvariant())),
            ManagedNameCasing.UpperFlat => string.Concat(words).ToUpperInvariant(),
            ManagedNameCasing.LowerFlat => string.Concat(words).ToLowerInvariant(),
            _ => throw new ArgumentOutOfRangeException(nameof(casing))
        };
        return char.IsDigit(value[0]) ? "_" + value : value;
    }
}
