using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Text.RegularExpressions;
using BGCS.Contracts.Requests;

namespace BGCS.NativeBuild.Execution;

/// <summary>Reads bounded compiler-authored dependency files without evaluating Make or command expressions.</summary>
public static partial class BuildDependencyCollector
{
    /// <summary>Reads source and header identities from each complete compiler dependency file.</summary>
    /// <param name="files">Compiler-authored lists with explicit formats and source identity.</param>
    /// <param name="workingDirectory">The origin for compiler-emitted relative paths.</param>
    /// <param name="cancellationToken">Cancels file collection.</param>
    /// <returns>Unique absolute dependencies in stable ordinal order.</returns>
    public static ImmutableArray<string> Read(
        IEnumerable<NativeDependencyFile> files,
        string workingDirectory,
        CancellationToken cancellationToken = default
    ) {
        HashSet<string> inputs = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (NativeDependencyFile file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileInfo info = new(file.path);
            if (!info.Exists || info.Length is <= 0 or > 16 * 1024 * 1024) throw new InvalidDataException("A compiler dependency file is missing, empty or exceeds its bounded size: " + file.path);
            string text = File.ReadAllText(file.path, Encoding.UTF8);
            IEnumerable<string> paths = file.format switch
            {
                NativeDependencyFormat.Make => ParseMake(text),
                NativeDependencyFormat.MsvcJson => ParseMsvc(text),
                _ => throw new NotSupportedException("The compiler dependency format is unknown.")
            };
            inputs.Add(Path.GetFullPath(file.source));
            foreach (string path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string absolute = Path.GetFullPath(path, file.workingDirectory ?? workingDirectory);
                if (!File.Exists(absolute)) throw new FileNotFoundException("The compiler reported a dependency that no longer exists.", absolute);
                inputs.Add(absolute);
                if (inputs.Count > 100_000) throw new InvalidDataException("The native compiler dependency closure exceeds 100,000 files.");
            }
        }
        return inputs.Order(StringComparer.Ordinal).ToImmutableArray();
    }

    /// <summary>Parses one Make dependency rule, including compiler escaping of spaces and line continuations.</summary>
    /// <param name="text">The complete bounded compiler output.</param>
    /// <returns>The dependency tokens, preserving literal filesystem characters.</returns>
    public static ImmutableArray<string> ParseMake(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        int separator = -1;
        bool escaped = false;
        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];
            if (escaped) { escaped = false; continue; }
            if (character == '\\') { escaped = true; continue; }
            if (character == ':' && !(index > 0 && char.IsAsciiLetter(text[index - 1]) && index + 1 < text.Length && text[index + 1] is '/' or '\\')) { separator = index; break; }
        }
        if (separator < 0) throw new InvalidDataException("The compiler dependency file contains no Make target separator.");
        ImmutableArray<string>.Builder result = ImmutableArray.CreateBuilder<string>();
        StringBuilder token = new();
        for (int index = separator + 1; index < text.Length; index++)
        {
            char character = text[index];
            if (character == '\\' && index + 1 < text.Length)
            {
                char next = text[index + 1];
                if (next is '\r' or '\n')
                {
                    index++;
                    if (next == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
                    continue;
                }
                if (char.IsWhiteSpace(next) || next is '\\' or '#' or ':' or '$') { token.Append(next); index++; continue; }
            }
            if (character == '$' && index + 1 < text.Length && text[index + 1] == '$') { token.Append('$'); index++; continue; }
            if (character == '#') { while (index < text.Length && text[index] != '\n') index++; character = '\n'; }
            if (char.IsWhiteSpace(character))
            {
                if (token.Length > 0) { result.Add(token.ToString()); token.Clear(); }
                continue;
            }
            token.Append(character);
        }
        if (token.Length > 0) result.Add(token.ToString());
        if (result.Count == 0) throw new InvalidDataException("The compiler dependency rule is empty.");
        return result.ToImmutable();
    }

    private static ImmutableArray<string> ParseMsvc(string text)
    {
        using JsonDocument json = JsonDocument.Parse(text, new() { MaxDepth = 32 });
        JsonElement root = json.RootElement;
        if (!root.TryGetProperty("Version", out JsonElement version) || version.GetString() is not ("1.0" or "1.1" or "1.2")
            || !root.TryGetProperty("Data", out JsonElement data) || data.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The MSVC source dependency schema is unknown or incomplete.");
        ImmutableArray<string>.Builder result = ImmutableArray.CreateBuilder<string>();
        result.Add(data.GetProperty("Source").GetString() ?? throw new InvalidDataException("MSVC dependency Source is null."));
        foreach (JsonElement include in data.GetProperty("Includes").EnumerateArray()) result.Add(include.GetString() ?? throw new InvalidDataException("MSVC dependency Include is null."));
        foreach (string property in new[] { "ImportedModules", "ImportedHeaderUnits" })
        {
            if (!data.TryGetProperty(property, out JsonElement modules)) continue;
            foreach (JsonElement module in modules.EnumerateArray())
            {
                if (!module.TryGetProperty("BMI", out JsonElement bmi) || bmi.GetString() is not string path) throw new InvalidDataException("An imported MSVC module has no frozen BMI dependency.");
                result.Add(path);
            }
        }
        return result.ToImmutable();
    }

    /// <summary>Collects exact existing files reported by the real linker's resolved-input trace.</summary>
    /// <param name="text">Complete linker standard output and error.</param>
    /// <param name="format">The explicitly selected native linker trace format.</param>
    /// <param name="workingDirectory">The origin for any relative trace path.</param>
    /// <returns>All read or searched library/object/script files in stable order.</returns>
    public static ImmutableArray<string> ReadLinkTrace(
        string text,
        NativeLinkTraceFormat format,
        string workingDirectory
    ) {
        ArgumentNullException.ThrowIfNull(text);
        IEnumerable<string> reported = format switch
        {
            NativeLinkTraceFormat.MsvcLibrarySearch => LibraryPathRegex().Matches(text).Select(match => match.Groups["path"].Value),
            NativeLinkTraceFormat.GnuLike => text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(line => NormalizeTracePath(line, workingDirectory)).Where(path => path is not null).Select(path => path!),
            _ => throw new NotSupportedException("The native linker trace format is unknown.")
        };
        HashSet<string> paths = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (string path in reported)
        {
            string absolute = Path.GetFullPath(path, workingDirectory);
            if (File.Exists(absolute)) paths.Add(absolute);
            else throw new FileNotFoundException("A resolved linker input no longer exists.", absolute);
            if (paths.Count > 100_000) throw new InvalidDataException("The linker dependency closure exceeds 100,000 files.");
        }
        if (paths.Count == 0) throw new InvalidDataException("The linker returned no resolvable native dependency trace; input completeness cannot be verified.");
        return paths.Order(StringComparer.Ordinal).ToImmutableArray();
    }
    private static string? NormalizeTracePath(
        string line,
        string workingDirectory
    ) {
        // The linker has an explicit invocation directory. The generator process may
        // have a different current directory, including when invoked through MSBuild.
        // Prefer the complete literal path before interpreting archive-member syntax.
        if (File.Exists(Path.Combine(workingDirectory, line))) return line;
        if (line.StartsWith('(') && line.IndexOf(')') is int closing && closing > 1) return line[1..closing];
        if (line.Contains('(') && line.EndsWith(')')) line = line[..line.LastIndexOf('(')];
        return Path.IsPathFullyQualified(line) || File.Exists(Path.Combine(workingDirectory, line)) ? line : null;
    }
    [GeneratedRegex(@"(?<path>(?:[A-Za-z]:[\\/]|\\\\)[^\r\n""]+?\.lib)(?=[""\t :]*\r?$)", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LibraryPathRegex();
}
