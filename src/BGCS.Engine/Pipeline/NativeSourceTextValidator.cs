using System;
using System.IO;
using System.Text.RegularExpressions;

namespace BGCS;

/// <summary>Rejects literal absolute includes without rewriting user-owned source or interpreting its preprocessor.</summary>
internal static class NativeSourceTextValidator
{
    internal static void Validate(
        string source,
        string origin
    ) {
        string text = source.Replace("\\\r\n", string.Empty, StringComparison.Ordinal).Replace("\\\n", string.Empty, StringComparison.Ordinal);
        bool block = false;
        bool lineStart = true;
        char quote = '\0';
        string? rawEnd = null;
        for (int index = 0; index < text.Length; index++)
        {
            char current = text[index];
            char next = index + 1 < text.Length ? text[index + 1] : '\0';
            if (rawEnd is not null)
            {
                if (text.AsSpan(index).StartsWith(rawEnd, StringComparison.Ordinal)) { index += rawEnd.Length - 1; rawEnd = null; }
                continue;
            }
            if (block)
            {
                if (current == '*' && next == '/') { block = false; index++; }
                else if (current == '\n') lineStart = true;
                continue;
            }
            if (quote != '\0')
            {
                if (current == '\\') index++;
                else if (current == quote) quote = '\0';
                continue;
            }
            if (current == '\n') { lineStart = true; continue; }
            if (current == '/' && next == '*') { block = true; index++; continue; }
            if (current == '/' && next == '/')
            {
                while (index + 1 < text.Length && text[index + 1] != '\n') index++;
                continue;
            }
            if (char.IsWhiteSpace(current)) continue;
            // An apostrophe inside a preprocessing number is a digit separator,
            // not the beginning of a character literal that can span later directives.
            if (char.IsAsciiDigit(current) || current == '.' && char.IsAsciiDigit(next))
            {
                lineStart = false;
                while (index + 1 < text.Length)
                {
                    char following = text[index + 1];
                    bool separator = following == '\'' && index + 2 < text.Length
                        && (char.IsAsciiLetterOrDigit(text[index + 2]) || text[index + 2] == '_');
                    bool exponentSign = (following is '+' or '-') && (text[index] is 'e' or 'E' or 'p' or 'P');
                    if (!char.IsAsciiLetterOrDigit(following) && following is not ('_' or '.') && !separator && !exponentSign) break;
                    index++;
                }
                continue;
            }
            if (lineStart && current == '#')
            {
                int end = text.IndexOf('\n', index);
                string directive = text[index..(end < 0 ? text.Length : end)];
                Match include = Regex.Match(directive, "^#(?:\\s|/\\*.*?\\*/)*include(?:_next)?(?:\\s|/\\*.*?\\*/)*[\"<]([^\">]+)[\">]", RegexOptions.CultureInvariant);
                if (include.Success)
                {
                    string path = include.Groups[1].Value;
                    if (path.StartsWith('/') || path.StartsWith('\\') || path.Contains(':'))
                        throw new IOException("Native source '" + origin + "' contains a nonportable absolute include '" + path
                            + "'. Use a declared source.includeDirectories root and a relative include; BGCS does not rewrite user source.");
                }
            }
            lineStart = false;
            if (current == 'R' && next == '"')
            {
                int open = text.IndexOf('(', index + 2);
                if (open >= index + 2 && open <= index + 18)
                { rawEnd = ")" + text[(index + 2)..open] + "\""; index = open; continue; }
            }
            if (current is '"' or '\'') quote = current;
        }
    }
}
