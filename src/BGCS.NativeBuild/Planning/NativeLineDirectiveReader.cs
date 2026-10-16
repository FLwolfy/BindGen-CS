using System;
using System.Collections.Generic;
using System.Text;

namespace BGCS.NativeBuild.Planning;

// Visits only compiler line metadata outside language literals and comments.
// In particular a raw string can contain an otherwise convincing complete
// line directive; its bytes must never participate in path normalization.
internal static class NativeLineDirectiveReader
{
    private static readonly UTF8Encoding s_utf8 = new(false, true);

    internal readonly record struct Directive(
        int start,
        int length,
        int fileStart,
        int fileLength,
        string lineNumber,
        string file,
        bool msvc
    );

    internal static string Rewrite(
        string source,
        Func<Directive, string?> replacement
    ) {
        StringBuilder result = new();
        int copied = 0;
        bool whitespaceOnly = true;
        for (int position = 0; position < source.Length;)
        {
            if (source[position] == '\\' && position + 1 < source.Length && source[position + 1] is '\r' or '\n')
            {
                position += 2;
                if (source[position - 1] == '\r' && position < source.Length && source[position] == '\n') position++;
                continue;
            }
            if (source[position] == '\n') { whitespaceOnly = true; position++; continue; }
            if (char.IsWhiteSpace(source[position])) { position++; continue; }
            if (source.AsSpan(position).StartsWith("//", StringComparison.Ordinal))
            {
                position = LineCommentEnd(source, position + 2); continue;
            }
            if (NativeMacroDirectiveReader.CommentEnd(source, position) is int comment)
            {
                if (source.AsSpan(position, comment - position).Contains('\n')) whitespaceOnly = true;
                position = comment; continue;
            }
            if (NativeMacroDirectiveReader.LiteralEnd(source, position) is int literal)
            { position = literal; whitespaceOnly = false; continue; }
            if (NativeMacroDirectiveReader.WordEnd(source, position) is int word)
            { position = word; whitespaceOnly = false; continue; }
            if (source[position] == '#' && whitespaceOnly && Read(source, position) is { } directive)
            {
                if (replacement(directive) is string text)
                {
                    result.Append(source, copied, position - copied).Append(text);
                    copied = position + directive.length;
                }
                position += directive.length; whitespaceOnly = false; continue;
            }
            whitespaceOnly = false; position++;
        }
        return copied == 0 ? source : result.Append(source, copied, source.Length - copied).ToString();
    }

    private static Directive? Read(
        string source,
        int start
    ) {
        int end = source.IndexOf('\n', start);
        if (end < 0) end = source.Length;
        if (end > start && source[end - 1] == '\r') end--;
        int position = start + 1;
        Skip();
        bool msvc = source.AsSpan(position).StartsWith("line", StringComparison.Ordinal);
        if (msvc)
        {
            position += 4;
            if (!Skip()) return null;
        }
        int number = position;
        while (position < end && char.IsAsciiDigit(source[position])) position++;
        int numberEnd = position;
        if (number == position || !Skip() || position >= end || source[position++] != '"') return null;
        string lineNumber = source[number..numberEnd];
        int fileStart = position;
        StringBuilder file = new();
        while (position < end && source[position] != '"')
        {
            char value = source[position++];
            if (value == '\\')
            {
                if (position < end && source[position] is >= '0' and <= '7')
                {
                    // Clang/GNU quote non-ASCII filename bytes as octal UTF-8.
                    // Decode only this metadata field, never language literals.
                    List<byte> bytes = [];
                    while (true)
                    {
                        int octet = 0, digits = 0;
                        while (position < end && digits < 3 && source[position] is >= '0' and <= '7')
                        {
                            octet = octet * 8 + source[position++] - '0';
                            digits++;
                        }
                        if (octet > byte.MaxValue || octet is 0 or 10 or 13) return null;
                        bytes.Add((byte)octet);
                        if (position + 1 >= end || source[position] != '\\' || source[position + 1] is not (>= '0' and <= '7')) break;
                        position++;
                    }
                    try { file.Append(s_utf8.GetString(bytes.ToArray())); }
                    catch (DecoderFallbackException) { return null; }
                    continue;
                }
                if (position >= end || source[position] is not ('\\' or '"')) return null;
                value = source[position++];
            }
            if (value is '\0' or '\r' or '\n') return null;
            file.Append(value);
        }
        if (position >= end) return null;
        int fileLength = position++ - fileStart;
        while (position < end)
        {
            if (!Skip()) return null;
            if (position == end) break;
            if (msvc || source[position] is not (>= '1' and <= '4')) return null;
            position++;
            if (position < end && source[position] is not (' ' or '\t')) return null;
        }
        return new(start, end - start, fileStart, fileLength, lineNumber, file.ToString(), msvc);

        bool Skip()
        {
            int previous = position;
            while (position < end && source[position] is ' ' or '\t') position++;
            return position != previous;
        }
    }

    private static int LineCommentEnd(
        string source,
        int position
    ) {
        while (position < source.Length)
        {
            if (source[position] == '\\' && position + 1 < source.Length && source[position + 1] is '\r' or '\n')
            {
                position += 2;
                if (source[position - 1] == '\r' && position < source.Length && source[position] == '\n') position++;
            }
            else if (source[position] == '\n') break;
            else position++;
        }
        return position;
    }
}
