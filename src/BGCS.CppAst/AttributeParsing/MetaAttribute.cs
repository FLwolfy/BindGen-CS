using System;
using System.Collections.Generic;
using System.Text;

namespace BGCS.CppAst.AttributeParsing;

/// <summary>
/// Retains an annotation feature name and its mutable named argument values.
/// </summary>
public sealed class MetaAttribute
{
    /// <summary>
    /// Stores the optional feature name preceding the named argument text.
    /// </summary>
    public string featureName = string.Empty;

    /// <summary>
    /// Stores scalar values or normalized class-expression text under ordinal argument names.
    /// </summary>
    public Dictionary<string, object> argumentMap = new(StringComparer.Ordinal);

    /// <summary>
    /// Formats the current feature and named arguments for diagnostic display.
    /// </summary>
    /// <returns>
    /// A display string in dictionary enumeration order, without a parsing or persistence guarantee.
    /// </returns>
    public override string ToString()
    {
        StringBuilder builder = new();
        builder.Append(featureName).Append(" {");
        foreach (KeyValuePair<string, object> argument in argumentMap)
        {
            builder.Append(argument.Key).Append(": ").Append(argument.Value).Append(", ");
        }
        return builder.Append('}').ToString();
    }

    /// <summary>
    /// Checks whether an argument is a true Boolean or the exact ordinal string "true".
    /// </summary>
    /// <param name="key">
    /// The argument name to query.
    /// </param>
    /// <returns>
    /// True for either supported true representation; false for missing or other values.
    /// </returns>
    public bool QueryKeyIsTrue(string key)
    {
        return argumentMap.TryGetValue(key, out object? value) && value is true or "true";
    }

    /// <summary>
    /// Checks whether every name in a nonempty argument selection has a supported true value.
    /// </summary>
    /// <param name="keys">
    /// The names to query, or null to request no selection.
    /// </param>
    /// <returns>
    /// True when all selected arguments are true; false for null, empty, missing, or false selections.
    /// </returns>
    public bool QueryKeysAreTrue(IReadOnlyList<string>? keys)
    {
        if (keys is null || keys.Count == 0)
        {
            return false;
        }
        foreach (string key in keys)
        {
            if (!QueryKeyIsTrue(key))
            {
                return false;
            }
        }
        return true;
    }
}
