using System;

namespace BGCS.CppAst.AttributeParsing;

/// <summary>
/// Decodes named annotations and the rmeta declaration-category envelope used in native source.
/// </summary>
public static class CustomAttributeTool
{
    /// <summary>
    /// Identifies the rmeta annotation envelope.
    /// </summary>
    public const string C_KMETALEADERWORD = "rmeta";
    /// <summary>
    /// Identifies class annotations within the envelope.
    /// </summary>
    public const string C_KMETACLASSLEADERWORD = "class";
    /// <summary>
    /// Identifies function annotations within the envelope.
    /// </summary>
    public const string C_KMETAFUNCTIONLEADERWORD = "function";
    /// <summary>
    /// Identifies field annotations within the envelope.
    /// </summary>
    public const string C_KMETAFIELDLEADERWORD = "field";
    /// <summary>
    /// Identifies enum annotations within the envelope.
    /// </summary>
    public const string C_KMETAENUMLEADERWORD = "enum";

    private const string C_SEPARATOR = "____";
    private const string C_PREFIX = C_KMETALEADERWORD + C_SEPARATOR;

    /// <summary>
    /// Checks the ordinal rmeta envelope prefix without parsing the category or arguments.
    /// </summary>
    /// <param name="meta">
    /// The annotation text to inspect.
    /// </param>
    /// <returns>
    /// True when the text starts with the exact rmeta envelope prefix.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// The annotation text is null.
    /// </exception>
    public static bool IsRstudioAttribute(string meta)
    {
        ArgumentNullException.ThrowIfNull(meta);
        return meta.StartsWith(C_PREFIX, StringComparison.Ordinal);
    }

    /// <summary>
    /// Parses an rmeta envelope for the requested declaration category.
    /// </summary>
    /// <param name="meta">
    /// Text containing the prefix, category, and feature with optional arguments after a vertical bar.
    /// </param>
    /// <param name="needLeaderWord">
    /// The exact ordinal declaration category to accept.
    /// </param>
    /// <param name="errorMessage">
    /// Receives parser diagnostics, or null on success or a category mismatch.
    /// </param>
    /// <returns>
    /// A parsed annotation, or null for another category, an absent envelope, or invalid arguments.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// The annotation text or requested category is null.
    /// </exception>
    public static MetaAttribute? ParseMetaStringFor(
        string meta,
        string needLeaderWord,
        out string? errorMessage
    ) {
        ArgumentNullException.ThrowIfNull(needLeaderWord);
        errorMessage = null;
        if (!IsRstudioAttribute(meta))
        {
            return null;
        }
        string[] parts = meta.Split(C_SEPARATOR, 3, StringSplitOptions.None);
        if (parts.Length != 3 || !string.Equals(parts[1], needLeaderWord, StringComparison.Ordinal))
        {
            return null;
        }
        int separator = parts[2].IndexOf('|');
        string feature = separator < 0 ? parts[2] : parts[2][..separator];
        string arguments = separator < 0 ? string.Empty : parts[2][(separator + 1)..];
        MetaAttribute? attribute = ParseMetaStringFor(arguments, out errorMessage);
        if (attribute is not null)
        {
            attribute.featureName = feature;
        }
        return attribute;
    }

    /// <summary>
    /// Parses plain named annotation arguments without an rmeta envelope.
    /// </summary>
    /// <param name="meta">
    /// The named argument text; empty text produces an empty annotation.
    /// </param>
    /// <param name="errorMessage">
    /// Receives parser diagnostics, or null on success.
    /// </param>
    /// <returns>
    /// An annotation with no feature name, or null for invalid argument syntax.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// The argument text is null.
    /// </exception>
    public static MetaAttribute? ParseMetaStringFor(
        string meta,
        out string? errorMessage
    ) {
        MetaAttribute attribute = new();
        return NamedParameterParser.ParseNamedParameters(meta, attribute.argumentMap, out errorMessage)
            ? attribute
            : null;
    }
}
