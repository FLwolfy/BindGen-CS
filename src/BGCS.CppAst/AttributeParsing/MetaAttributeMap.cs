using System;
using System.Collections.Generic;
using System.Globalization;

namespace BGCS.CppAst.AttributeParsing;

/// <summary>
/// Collects annotation arguments with first-declaration precedence and independent key ownership.
/// </summary>
public sealed class MetaAttributeMap
{
    private readonly Dictionary<string, object> m_arguments = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets whether no named arguments have been contributed.
    /// </summary>
    public bool isNull => m_arguments.Count == 0;

    /// <summary>
    /// Looks up the first contributed value under an ordinal argument name.
    /// </summary>
    /// <param name="argName">
    /// The argument name to query.
    /// </param>
    /// <returns>
    /// The retained value, or null when no value exists. Mutable caller-supplied values remain borrowed.
    /// </returns>
    public object? QueryArgument(string argName)
    {
        return m_arguments.TryGetValue(argName, out object? value) ? value : null;
    }

    /// <summary>
    /// Converts an argument to a Boolean using invariant conversion rules.
    /// </summary>
    /// <param name="argName">
    /// The argument name to query.
    /// </param>
    /// <param name="defaultVal">
    /// The value to use for absent arguments or unsupported scalar conversions.
    /// </param>
    /// <returns>
    /// The converted Boolean, or the default for missing or invalid scalar values.
    /// Exceptions other than conversion format, cast, or overflow errors propagate to the caller.
    /// </returns>
    public bool QueryArgumentAsBool(
        string argName,
        bool defaultVal
    ) {
        return ConvertArgument(argName, defaultVal, Convert.ToBoolean);
    }

    /// <summary>
    /// Converts an argument to a 32-bit signed integer using invariant conversion rules.
    /// </summary>
    /// <param name="argName">
    /// The argument name to query.
    /// </param>
    /// <param name="defaultVal">
    /// The value to use for absent arguments or unsupported scalar conversions.
    /// </param>
    /// <returns>
    /// The converted integer, or the default for missing, invalid, or overflowing values.
    /// Exceptions other than conversion format, cast, or overflow errors propagate to the caller.
    /// </returns>
    public int QueryArgumentAsInteger(
        string argName,
        int defaultVal
    ) {
        return ConvertArgument(argName, defaultVal, Convert.ToInt32);
    }

    /// <summary>
    /// Formats an argument as text using invariant conversion rules.
    /// </summary>
    /// <param name="argName">
    /// The argument name to query.
    /// </param>
    /// <param name="defaultVal">
    /// The text to use for absent arguments or unsupported scalar conversions.
    /// </param>
    /// <returns>
    /// The converted text, or the default for missing, null, or invalid conversions.
    /// Exceptions other than conversion format, cast, or overflow errors propagate to the caller.
    /// </returns>
    public string QueryArgumentAsString(
        string argName,
        string defaultVal
    ) {
        return ConvertArgument(argName, defaultVal, Convert.ToString) ?? defaultVal;
    }

    /// <summary>
    /// Copies previously unseen named arguments without modifying or retaining the source dictionary.
    /// </summary>
    /// <param name="metaAttr">
    /// The contribution to snapshot, or null to contribute nothing. Values themselves remain borrowed.
    /// </param>
    public void Append(MetaAttribute? metaAttr)
    {
        if (metaAttr is null)
        {
            return;
        }
        foreach (KeyValuePair<string, object> argument in metaAttr.argumentMap)
        {
            m_arguments.TryAdd(argument.Key, argument.Value);
        }
    }

    private T ConvertArgument<T>(
        string argName,
        T defaultValue,
        Func<object, IFormatProvider, T> convert
    ) {
        object? value = QueryArgument(argName);
        if (value is null)
        {
            return defaultValue;
        }
        try
        {
            return convert(value, CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            return defaultValue;
        }
    }
}
