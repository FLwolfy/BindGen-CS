using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BGCS.Frontend.Clang.Utilities;

/// <summary>Maps compiler file provenance for declared generated inputs and parsed project files.</summary>
internal sealed class SourcePathMapper
{
    private readonly Dictionary<string, string> m_paths = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly (string Physical, string Logical)[] m_spellings;
    private readonly (string Physical, string Logical)[] m_provenance;

    internal SourcePathMapper(
        IReadOnlyDictionary<string, string> paths,
        string pathBase = "",
        IEnumerable<string>? sourceFiles = null
    ) {
        ArgumentNullException.ThrowIfNull(paths);
        foreach ((string physical, string logical) in paths)
        {
            if (!Path.IsPathFullyQualified(physical) || string.IsNullOrWhiteSpace(logical) || logical.Contains('\\') || logical.Contains('\0')
                || Path.IsPathRooted(logical) || logical.Split('/').Any(part => part is "" or "." or ".." || part.Contains(':')))
                throw new ArgumentException("Generated source mappings require absolute physical files and portable logical file paths.", nameof(paths));
            if (!m_paths.TryAdd(Path.GetFullPath(physical), logical)) throw new ArgumentException("A generated physical file has duplicate logical source mappings.", nameof(paths));
        }
        if (!string.IsNullOrWhiteSpace(pathBase) && sourceFiles is not null)
        {
            string root = Path.GetFullPath(pathBase);
            foreach (string file in sourceFiles)
            {
                if (!Path.IsPathFullyQualified(file)) continue;
                string physical = Path.GetFullPath(file);
                string relative = Path.GetRelativePath(root, physical).Replace('\\', '/');
                // Keep external SDK paths distinct; an explicit generated mapping always wins.
                if (Path.IsPathRooted(relative) || relative.Split('/').Any(part => part is "" or "." or ".." || part.Contains(':'))) continue;
                m_paths.TryAdd(physical, relative);
            }
        }
        m_spellings = m_paths.SelectMany(pair => new[] { (pair.Key, pair.Value), (pair.Key.Replace('\\', '/'), pair.Value) })
            .Distinct().OrderByDescending(pair => pair.Item1.Length).ToArray();
        m_provenance = m_spellings.SelectMany(pair => new[] { "(anonymous at ", "(unnamed at ", "(lambda at ",
            "(unnamed struct at ", "(unnamed union at ", "(unnamed class at ",
            "(anonymous struct at ", "(anonymous union at ", "(anonymous class at " }
            .Select(prefix => (prefix + pair.Physical + ":", prefix + pair.Logical + ":"))).ToArray();
    }

    internal bool TryMapPath(
        string path,
        out string logical
    ) {
        logical = string.Empty;
        return !string.IsNullOrEmpty(path) && Path.IsPathFullyQualified(path) && m_paths.TryGetValue(Path.GetFullPath(path), out logical!);
    }

    internal string MapPath(string path) => TryMapPath(path, out string logical) ? logical : path;

    internal string MapDiagnostic(string text)
    {
        string[] lines = text.Split('\n');
        for (int index = 0; index < lines.Length; index++)
            foreach ((string physical, string logical) in m_spellings)
                foreach (string lead in new[] { string.Empty, "In file included from " })
                {
                    string prefix = lead + physical + ":";
                    if (lines[index].StartsWith(prefix, StringComparison.Ordinal)) lines[index] = lead + logical + ":" + lines[index][prefix.Length..];
                }
        return string.Join('\n', lines);
    }

    internal string MapCompilerProvenance(string text)
    {
        // A type spelling can also contain non-type template string arguments.
        // Their bytes are source semantics, even when they resemble compiler provenance.
        StringBuilder result = new();
        int start = 0;
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] is '"' or '\'') { index = LiteralEnd(text, index) - 1; continue; }
            if (text[index] != '(') continue;
            foreach ((string physical, string logical) in m_provenance)
            {
                if (!text.AsSpan(index).StartsWith(physical, StringComparison.Ordinal)) continue;
                result.Append(text.AsSpan(start, index - start)).Append(logical);
                index += physical.Length - 1;
                start = index + 1;
                break;
            }
        }
        result.Append(text.AsSpan(start));
        return result.ToString();
    }

    private static int LiteralEnd(
        string text,
        int quote
    ) {
        if (text[quote] == '"' && quote > 0 && text[quote - 1] == 'R')
        {
            int opening = text.IndexOf('(', quote + 1);
            if (opening >= 0 && opening - quote <= 17)
            {
                string delimiter = text[(quote + 1)..opening];
                if (!delimiter.Any(character => char.IsWhiteSpace(character) || character is '\\' or ')' or '"'))
                {
                    int closing = text.IndexOf(")" + delimiter + "\"", opening + 1, StringComparison.Ordinal);
                    if (closing >= 0) return closing + delimiter.Length + 2;
                }
            }
        }
        for (int index = quote + 1; index < text.Length; index++)
        {
            if (text[index] == '\\') { index++; continue; }
            if (text[index] == text[quote]) return index + 1;
        }
        return text.Length;
    }

    internal string MapUsr(
        string usr,
        string sourceFile,
        bool usePresumedIdentity = false
    ) {
        if (!TryMapPath(sourceFile, out string logical)) return usr;
        foreach (string file in new[] { Path.GetFullPath(sourceFile), Path.GetFullPath(sourceFile).Replace('\\', '/'), Path.GetFileName(sourceFile) })
        {
            foreach (string separator in new[] { "@", ":" })
            {
                string prefix = "c:" + file + separator;
                if (usr.StartsWith(prefix, StringComparison.Ordinal)) usr = "c:" + logical + separator + usr[prefix.Length..];
            }
            // Clang identifies an unnamed nested tag with an embedded filename
            // and byte offset, for example @S@header.hpp@54@FI@member. Rewrite
            // only this compiler-owned grammar, never arbitrary symbol text.
            foreach (string tag in new[] { "@S@", "@U@", "@E@" })
            {
                string prefix = tag + file + "@";
                int start = 0;
                while ((start = usr.IndexOf(prefix, start, StringComparison.Ordinal)) >= 0)
                {
                    int end = start + prefix.Length;
                    while (end < usr.Length && char.IsAsciiDigit(usr[end])) end++;
                    if (end > start + prefix.Length && (end == usr.Length || usr[end] == '@'))
                    {
                        usr = usr[..(start + tag.Length)] + logical + usr[(start + tag.Length + file.Length)..];
                        start += tag.Length + logical.Length + 1;
                    }
                    else start += prefix.Length;
                }
            }
        }
        if (usePresumedIdentity)
        {
            // A native preprocessor's line directives preserve the original
            // declaration positions, but Clang USRs still contain byte offsets
            // into the temporary aggregate. Those offsets change with path
            // lengths and unrelated preprocessed prefixes. The source graph's
            // named owner, original presumed anonymous-type spelling and
            // parameter index already distinguish these declarations.
            foreach (string prefix in new[] { "c:" + logical + "@", "@S@" + logical + "@", "@U@" + logical + "@", "@E@" + logical + "@" })
            {
                int start = 0;
                while ((start = usr.IndexOf(prefix, start, StringComparison.Ordinal)) >= 0)
                {
                    int end = start + prefix.Length;
                    while (end < usr.Length && char.IsAsciiDigit(usr[end])) end++;
                    if (end > start + prefix.Length && (end == usr.Length || usr[end] == '@'))
                        usr = usr[..(start + prefix.Length)] + (end < usr.Length ? usr[(end + 1)..] : string.Empty);
                    start += prefix.Length;
                }
            }
        }
        return MapCompilerProvenance(usr);
    }
}
