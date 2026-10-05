using System;
using System.Collections.Concurrent;
using System.Text;
using BGCS.Configuration.Naming;
using BGCS.Conversion;

namespace BGCS.Configuration;

/// <summary>
/// Provides invariant managed identifier normalization and applies the current mutable replacement policy on each lookup.
/// </summary>
public partial class CsCodeGeneratorConfig
{
    private readonly ConcurrentDictionary<string, string> m_nameCache = new();
    /// <summary>
    /// Resolves explicit type mappings or normalizes an identifier into a Pascal-style managed stem.
    /// </summary>
    /// <param name="name">
    /// The original native identifier. Underscores delimit words and a trailing type suffix may be removed.
    /// </param>
    /// <returns>
    /// The mapped or normalized identifier with current name replacements applied; an empty input remains empty.
    /// </returns>
    public string GetCsCleanName(string name)
    {
        if (this.typeMappings.TryGetValue(name, out string? mappedName))
        {
            return mappedName;
        }

        string cacheKey = $"clean:{name}";
        if (this.m_nameCache.TryGetValue(cacheKey, out string? cacheEntry))
        {
            return ApplyNameMappings(cacheEntry);
        }

        StringBuilder sb = new();
        bool isCaps = name.IsCaps();
        bool wasLowerCase = false;
        bool wasNumber = false;
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (c == '_')
            {
                wasLowerCase = true;
                continue;
            }

            if (isCaps)
            {
                c = char.ToLowerInvariant(c);
            }

            if (i == 0)
            {
                c = char.ToUpperInvariant(c);
            }

            if (wasLowerCase || wasNumber)
            {
                c = char.ToUpperInvariant(c);
                wasLowerCase = false;
            }

            sb.Append(c);
            wasNumber = char.IsDigit(c);
        }

        if (sb.Length > 1 && sb[^1] == 'T')
        {
            var c = sb[^2];
            if (char.IsLower(c) || c == '_')
            {
                sb.Remove(sb.Length - 1, 1);
            }
        }

        string newName = sb.ToString();
        this.m_nameCache.TryAdd(cacheKey, newName);
        return ApplyNameMappings(newName);
    }

    /// <summary>
    /// Resolves explicit type mappings or converts a native identifier with the selected naming convention.
    /// </summary>
    /// <param name="name">
    /// The original native identifier.
    /// </param>
    /// <param name="convention">
    /// The managed word-casing and separator policy.
    /// </param>
    /// <param name="removeTailingT">
    /// Whether a final uppercase T following a lowercase character or underscore is removed.
    /// </param>
    /// <returns>
    /// The mapped or normalized identifier with current replacements applied; cached stems do not capture mutable mappings.
    /// </returns>
    public string GetCsCleanNameWithConvention(
        string name,
        NamingConvention convention,
        bool removeTailingT = true
    ) {
        if (this.typeMappings.TryGetValue(name, out string? mappedName))
        {
            return mappedName;
        }

        string cacheKey = $"conv:{convention}:{removeTailingT}:{name}";
        if (this.m_nameCache.TryGetValue(cacheKey, out string? cacheEntry))
        {
            return ApplyNameMappings(cacheEntry);
        }

        string newName = NamingHelper.ConvertTo(name, convention);
        if (removeTailingT)
        {
            if (newName.Length > 1 && newName[^1] == 'T')
            {
                var c = newName[^2];
                if (char.IsLower(c) || c == '_')
                {
                    newName = newName.Remove(newName.Length - 1, 1);
                }
            }
        }

        this.m_nameCache.TryAdd(cacheKey, newName);
        return ApplyNameMappings(newName);
    }

    internal string GetManagedTypeName(string nativeName)
    {
        if (TryGetTypeMapping(nativeName, out var mapping) && !string.IsNullOrWhiteSpace(mapping.friendlyName))
            return mapping.friendlyName;
        if (this.typeNamingConvention == NamingConvention.PascalCase)
            return GetCsCleanName(nativeName);
        return GetCsCleanNameWithConvention(nativeName, this.typeNamingConvention);
    }

    internal string GetManagedHandleName(string nativeName)
    {
        if (TryGetHandleMapping(nativeName, out var mapping) && !string.IsNullOrWhiteSpace(mapping.friendlyName))
            return mapping.friendlyName;
        if (this.handleNamingConvention == NamingConvention.PascalCase)
            return GetCsCleanName(nativeName);
        return GetCsCleanNameWithConvention(nativeName, this.handleNamingConvention);
    }

    internal string GetManagedEnumName(string nativeName)
    {
        if (TryGetEnumMapping(nativeName, out var mapping) && !string.IsNullOrWhiteSpace(mapping.friendlyName))
            return mapping.friendlyName;
        if (this.enumNamingConvention == NamingConvention.PascalCase)
            return GetCsCleanName(nativeName);
        return GetCsCleanNameWithConvention(nativeName, this.enumNamingConvention);
    }

    private string ApplyNameMappings(string name)
    {
        foreach (var mapping in this.nameMappings)
        {
            name = name.Replace(mapping.Key, mapping.Value, StringComparison.InvariantCultureIgnoreCase);
        }
        return name;
    }
}
