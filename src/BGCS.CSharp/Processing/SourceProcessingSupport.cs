using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using BGCS.Contracts.Common;
using BGCS.Contracts.Plugins;

namespace BGCS.CSharp.Processing;

internal static class SourceProcessingSupport
{
    internal const int C_MAXIMUM_TEXT = 64 * 1024 * 1024;

    internal static void Identity(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (id.Length > 128 || id.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or '-' or '/')))
            throw new ArgumentException("A processor identity must use at most 128 stable ASCII identity characters.", nameof(id));
    }

    internal static CacheFingerprintContribution Fingerprint(object settings)
        => new(new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(settings)))), []);

    internal static void Append(
        StringBuilder output,
        ReadOnlySpan<char> text
    ) {
        if (text.Length > C_MAXIMUM_TEXT - output.Length) throw new InvalidOperationException("A source processor exceeded its bounded candidate text size.");
        output.Append(text);
    }

    internal static string Literal(
        string input,
        string match,
        string replacement,
        StringComparison comparison,
        CancellationToken cancellationToken,
        out bool changed
    ) {
        StringBuilder output = new();
        int start = 0, next;
        changed = false;
        while ((next = input.IndexOf(match, start, comparison)) >= 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            changed = true;
            Append(output, input.AsSpan(start, next - start));
            Append(output, replacement);
            start = next + match.Length;
        }
        Append(output, input.AsSpan(start));
        return output.ToString();
    }

    internal static void Replacement(
        StringBuilder output,
        string template,
        Match match,
        string input,
        HashSet<string> groups,
        int lastGroup
    ) {
        for (int index = 0; index < template.Length; index++)
        {
            if (template[index] != '$' || index + 1 == template.Length) { Append(output, template.AsSpan(index, 1)); continue; }
            char next = template[index + 1];
            switch (next)
            {
                case '$': Append(output, "$".AsSpan()); index++; continue;
                case '&': Append(output, match.ValueSpan); index++; continue;
                case '`': Append(output, input.AsSpan(0, match.Index)); index++; continue;
                case '\'': Append(output, input.AsSpan(match.Index + match.Length)); index++; continue;
                case '_': Append(output, input.AsSpan()); index++; continue;
                case '+': Append(output, match.Groups[lastGroup].ValueSpan); index++; continue;
            }
            int end = index + 1;
            string? group = null;
            if (next == '{')
            {
                end = template.IndexOf('}', index + 2);
                if (end >= 0)
                {
                    group = template[(index + 2)..end];
                    if (int.TryParse(group, NumberStyles.None, CultureInfo.InvariantCulture, out int numeric)) group = numeric.ToString(CultureInfo.InvariantCulture);
                }
            }
            else if (char.IsAsciiDigit(next))
            {
                while (end < template.Length && char.IsAsciiDigit(template[end])) end++;
                if (int.TryParse(template.AsSpan(index + 1, end - index - 1), NumberStyles.None, CultureInfo.InvariantCulture, out int number))
                    group = number.ToString(CultureInfo.InvariantCulture);
                end--;
            }
            if (group is not null && groups.Contains(group))
            {
                Group captured = int.TryParse(group, NumberStyles.None, CultureInfo.InvariantCulture, out int number)
                    ? match.Groups[number] : match.Groups[group];
                Append(output, captured.ValueSpan);
                index = end;
            }
            else Append(output, "$".AsSpan());
        }
    }
}
