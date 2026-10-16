using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace BGCS.NativeBuild.Planning;

// A preprocessing-directive reader, not a C/C++ declaration parser. Origins are
// accepted only when complete replacement-token sequences match exactly.
internal static class NativeMacroDirectiveReader
{
    internal sealed record Directive(
        string operation,
        string name,
        string text,
        int start,
        int length,
        int line
    );

    internal static ImmutableArray<Directive> ReadDirectives(string source)
    {
        var directives = ImmutableArray.CreateBuilder<Directive>();
        _ = ReadCore(source, false, true, null, directives);
        return directives.ToImmutable();
    }
    internal sealed record Definition(
        string name,
        string replacement,
        bool functionLike,
        ImmutableArray<string> parameters,
        string tokens,
        int line,
        int start,
        int length
    );

    internal static ImmutableArray<Definition> Read(
        string original,
        bool msvcMacroDump = false,
        bool rawStrings = true
    ) => ReadCore(original, msvcMacroDump, rawStrings, null);

    // Even inactive directives disqualify command-line provenance: identical final
    // tokens cannot prove which definition survived conditional preprocessing.
    internal static ImmutableHashSet<string> ChangedNames(
        string original,
        bool rawStrings
    ) {
        HashSet<string> names = new(StringComparer.Ordinal);
        _ = ReadCore(original, false, rawStrings, names);
        return names.ToImmutableHashSet(StringComparer.Ordinal);
    }

    private static ImmutableArray<Definition> ReadCore(
        string original,
        bool msvcMacroDump,
        bool rawStrings,
        ISet<string>? changedNames,
        ImmutableArray<Directive>.Builder? directives = null
    ) {
        StringBuilder joined = new();
        List<int> offsets = [], lines = [];
        int physicalLine = 1;
        for (int position = 0; position < original.Length; position++)
        {
            if (original[position] == '\\' && position + 1 < original.Length && original[position + 1] is '\r' or '\n')
            {
                position++;
                if (original[position] == '\r' && position + 1 < original.Length && original[position + 1] == '\n') position++;
                physicalLine++;
                continue;
            }
            offsets.Add(position); lines.Add(physicalLine); joined.Append(original[position]);
            if (original[position] == '\n') physicalLine++;
        }
        string text = joined.ToString();
        if (rawStrings) RestoreRawSplices(original, ref text, offsets, lines);
        ImmutableArray<Definition>.Builder result = ImmutableArray.CreateBuilder<Definition>();
        bool whitespaceOnly = true;
        for (int position = 0; position < text.Length;)
        {
            if (text[position] == '\n') { whitespaceOnly = true; position++; continue; }
            if (char.IsWhiteSpace(text[position])) { position++; continue; }
            if (CommentEnd(text, position) is int comment)
            {
                if (text.AsSpan(position, comment - position).Contains('\n')) whitespaceOnly = true;
                position = comment; continue;
            }
            if (LiteralEnd(text, position, rawStrings) is int literal) { position = literal; whitespaceOnly = false; continue; }
            if (WordEnd(text, position) is int word) { position = word; whitespaceOnly = false; continue; }
            if (text[position] != '#' || !whitespaceOnly) { whitespaceOnly = false; position++; continue; }
            int start = position++, firstLine = lines[start];
            while (position < text.Length && text[position] != '\n')
            {
                if (LiteralEnd(text, position, rawStrings) is int quoted) position = quoted;
                else if (CommentEnd(text, position) is int end) position = end;
                else if (WordEnd(text, position) is int value) position = value;
                else position++;
            }
            int endOffset = position < offsets.Count ? offsets[position] : original.Length;
            string directive = text[start..position];
            string operation = Operation(directive);
            if (operation is "define" or "undef" or "pragma")
                directives?.Add(new(operation, ChangedName(directive) ?? string.Empty,
                    original.Substring(offsets[start], endOffset - offsets[start]), offsets[start], endOffset - offsets[start], firstLine));
            if (changedNames is not null && ChangedName(directive) is string changed) changedNames.Add(changed);
            if (Parse(directive, firstLine, offsets[start], endOffset - offsets[start], msvcMacroDump, rawStrings) is { } definition) result.Add(definition);
            whitespaceOnly = false;
        }
        return result.ToImmutable();
    }

    private static string Operation(string directive)
    {
        int position = 1;
        while (position < directive.Length)
        {
            if (char.IsWhiteSpace(directive[position])) position++;
            else if (CommentEnd(directive, position) is int comment) position = comment;
            else break;
        }
        int start = position;
        while (position < directive.Length && (char.IsAsciiLetter(directive[position]) || directive[position] == '_')) position++;
        return directive[start..position];
    }

    private static string? ChangedName(string directive)
    {
        int position = 1;
        Skip();
        string operation = Identifier();
        if (operation is not ("define" or "undef")) return null;
        Skip();
        string name = Identifier();
        return name.Length == 0 ? null : name;

        void Skip()
        {
            while (position < directive.Length)
            {
                if (char.IsWhiteSpace(directive[position])) position++;
                else if (CommentEnd(directive, position) is int comment) position = comment;
                else break;
            }
        }
        string Identifier()
        {
            int begin = position;
            if (position < directive.Length && (char.IsLetter(directive[position]) || directive[position] == '_'))
            {
                position++;
                while (position < directive.Length && (char.IsLetterOrDigit(directive[position]) || directive[position] == '_')) position++;
            }
            return directive[begin..position];
        }
    }

    private static Definition? Parse(
        string directive,
        int line,
        int start,
        int length,
        bool msvcMacroDump,
        bool rawStrings
    ) {
        int position = 1;
        Skip();
        if (Identifier() != "define") return null;
        Skip();
        string name = Identifier();
        if (name.Length == 0) return null;
        bool function = position < directive.Length && directive[position] == '(';
        ImmutableArray<string> parameters = [];
        if (function)
        {
            int end = directive.IndexOf(')', position + 1);
            if (end < 0) return null;
            string arguments = directive[(position + 1)..end].Trim();
            parameters = arguments.Length == 0 ? [] : ImmutableArray.CreateRange(arguments.Split(',', StringSplitOptions.TrimEntries));
            // MSVC /PD prints a zero-named-parameter variadic macro as `(, ...)`.
            // This is dump formatting, not a legal empty source parameter. Normalize
            // only the compiler dump; real source definitions remain exact evidence.
            if (msvcMacroDump && parameters is ["", "..."]) parameters = ["..."];
            position = end + 1;
        }
        string replacement = directive[position..].Trim();
        string? tokens = Tokens(replacement, rawStrings);
        return tokens is null ? null : new(name, replacement, function, parameters, tokens, line, start, length);

        void Skip()
        {
            while (position < directive.Length)
            {
                if (char.IsWhiteSpace(directive[position])) position++;
                else if (CommentEnd(directive, position) is int comment) position = comment;
                else break;
            }
        }
        string Identifier()
        {
            int begin = position;
            if (position < directive.Length && (char.IsLetter(directive[position]) || directive[position] == '_'))
            {
                position++;
                while (position < directive.Length && (char.IsLetterOrDigit(directive[position]) || directive[position] == '_')) position++;
            }
            return directive[begin..position];
        }
    }

    private static string? Tokens(
        string text,
        bool rawStrings
    ) {
        StringBuilder result = new();
        for (int position = 0; position < text.Length;)
        {
            if (char.IsWhiteSpace(text[position])) { position++; continue; }
            if (CommentEnd(text, position) is int comment) { position = comment; continue; }
            int start = position;
            if (LiteralEnd(text, position, rawStrings) is int literal) position = literal;
            else if (WordEnd(text, position) is int word) position = word;
            else
            {
                string? punctuation = null;
                foreach (string candidate in Punctuators)
                    if (text.AsSpan(position).StartsWith(candidate, StringComparison.Ordinal)) { punctuation = candidate; break; }
                position += punctuation?.Length ?? 1;
            }
            result.Append(position - start).Append(':').Append(text.AsSpan(start, position - start)).Append(';');
        }
        return result.ToString();
    }

    internal static int? WordEnd(
        string text,
        int position
    ) {
        if (char.IsLetter(text[position]) || text[position] == '_')
        {
            while (position < text.Length && (char.IsLetterOrDigit(text[position]) || text[position] == '_')) position++;
            return position;
        }
        if (!char.IsDigit(text[position]) && !(text[position] == '.' && position + 1 < text.Length && char.IsDigit(text[position + 1]))) return null;
        position++;
        while (position < text.Length)
        {
            char character = text[position];
            if (char.IsLetterOrDigit(character) || character is '_' or '.' or '\'') position++;
            else if (character is '+' or '-' && text[position - 1] is 'e' or 'E' or 'p' or 'P') position++;
            else break;
        }
        return position;
    }

    internal static int? CommentEnd(
        string text,
        int position
    ) {
        if (position + 1 >= text.Length || text[position] != '/') return null;
        if (text[position + 1] == '/')
        {
            int end = text.IndexOf('\n', position + 2);
            return end < 0 ? text.Length : end;
        }
        if (text[position + 1] != '*') return null;
        int closing = text.IndexOf("*/", position + 2, StringComparison.Ordinal);
        return closing < 0 ? text.Length : closing + 2;
    }

    internal static int? LiteralEnd(
        string text,
        int position,
        bool rawStrings = true
    ) {
        foreach (string prefix in LiteralPrefixes)
        {
            if (!text.AsSpan(position).StartsWith(prefix, StringComparison.Ordinal)) continue;
            int content = position + prefix.Length;
            if (prefix.EndsWith("R\"", StringComparison.Ordinal))
            {
                if (!rawStrings) continue;
                int opening = text.IndexOf('(', content);
                if (opening < 0 || opening - content > 16) return text.Length;
                string delimiter = text[content..opening];
                int closing = text.IndexOf(")" + delimiter + "\"", opening + 1, StringComparison.Ordinal);
                if (closing < 0) return text.Length;
                int end = closing + delimiter.Length + 2;
                while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_')) end++;
                return end;
            }
            char quote = prefix[^1];
            while (content < text.Length)
            {
                if (text[content] == '\\') { content += Math.Min(2, text.Length - content); continue; }
                if (text[content++] == quote)
                {
                    while (content < text.Length && (char.IsLetterOrDigit(text[content]) || text[content] == '_')) content++;
                    return content;
                }
            }
            return text.Length;
        }
        return null;
    }

    private static void RestoreRawSplices(
        string original,
        ref string text,
        List<int> offsets,
        List<int> lines
    ) {
        List<(int begin, int end, int physicalBegin, int physicalEnd, int line)> bodies = [];
        for (int position = 0; position < text.Length;)
        {
            if (CommentEnd(text, position) is int comment) { position = comment; continue; }
            string? prefix = null;
            foreach (string candidate in LiteralPrefixes)
                if (candidate.EndsWith("R\"", StringComparison.Ordinal) && text.AsSpan(position).StartsWith(candidate, StringComparison.Ordinal)) { prefix = candidate; break; }
            if (prefix is not null)
            {
                int content = position + prefix.Length, opening = text.IndexOf('(', content);
                if (opening >= 0 && opening - content <= 16)
                {
                    int physicalOpening = offsets[opening], delimiterBegin = offsets[content - 1] + 1;
                    string delimiter = original[delimiterBegin..physicalOpening];
                    if (delimiter == text[content..opening] && delimiter.IndexOfAny([' ', '\t', '\r', '\n', '\\', ')']) < 0)
                    {
                        // Find the closing delimiter in physical raw content. Splicing
                        // may otherwise manufacture a false closing delimiter in the
                        // phase-two view before the real physical closing delimiter.
                        int closing = original.IndexOf(")" + delimiter + "\"", physicalOpening + 1, StringComparison.Ordinal);
                        if (closing >= 0)
                        {
                            int end = offsets.BinarySearch(closing), closingQuote = offsets.BinarySearch(closing + delimiter.Length + 1);
                            if (end >= 0 && closingQuote >= 0)
                            {
                                bodies.Add((opening + 1, end, physicalOpening + 1, closing, lines[opening]));
                                position = closingQuote + 1;
                                continue;
                            }
                        }
                    }
                }
            }
            if (LiteralEnd(text, position) is int literal) position = literal;
            else if (WordEnd(text, position) is int word) position = word;
            else position++;
        }
        if (bodies.Count == 0) return;
        string joinedText = text;
        StringBuilder restored = new();
        List<int> restoredOffsets = [], restoredLines = [];
        int copied = 0;
        foreach (var body in bodies)
        {
            CopyJoined(body.begin);
            int line = body.line;
            for (int position = body.physicalBegin; position < body.physicalEnd; position++)
            {
                restored.Append(original[position]); restoredOffsets.Add(position); restoredLines.Add(line);
                if (original[position] == '\n') line++;
            }
            copied = body.end;
        }
        CopyJoined(joinedText.Length);
        text = restored.ToString();
        offsets.Clear(); offsets.AddRange(restoredOffsets);
        lines.Clear(); lines.AddRange(restoredLines);

        void CopyJoined(int end)
        {
            for (; copied < end; copied++)
            {
                restored.Append(joinedText[copied]); restoredOffsets.Add(offsets[copied]); restoredLines.Add(lines[copied]);
            }
        }
    }

    private static readonly string[] LiteralPrefixes = ["u8R\"", "uR\"", "UR\"", "LR\"", "R\"", "u8\"", "u8'", "u\"", "U\"", "L\"", "u'", "U'", "L'", "\"", "'"];
    private static readonly string[] Punctuators = ["%:%:", ">>=", "<<=", "->*", "...", "<=>", "##", "::", ".*", "->", "++", "--", "<<", ">>", "<=", ">=", "==", "!=", "&&", "||", "+=", "-=", "*=", "/=", "%=", "&=", "^=", "|=", "<:", ":>", "<%", "%>", "%:"];
}
