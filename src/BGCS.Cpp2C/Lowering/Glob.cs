using System;
using System.Text.RegularExpressions;

namespace BGCS.Cpp2C.Lowering;

internal static partial class Glob
{
    internal static bool IsMatch(
        string value,
        string pattern
    ) {
        if (string.IsNullOrWhiteSpace(pattern))
            return false;
        string expression = "^" + Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal) + "$";
        return Regex.IsMatch(value.Replace(" ", string.Empty, StringComparison.Ordinal), expression.Replace("\\ ", string.Empty, StringComparison.Ordinal), RegexOptions.CultureInvariant);
    }
}
