using BGCS.Infrastructure.IO;
using BGCS.Infrastructure.Workers;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Security.Cryptography;
using System.Linq;
using System.Text;
using System.Threading;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Hosting;

namespace BGCS.Frontend.Clang.Parsing.Arguments;

/// <summary>Preserves explicit driver input ordering and immutable external argument-file dependencies.</summary>
/// <param name="arguments">Expanded compile arguments, with Clang configuration options preceding explicit command-line options.</param>
/// <param name="linkerOnlyArguments">Clang configuration dollar-prefixed options appended only during final linking.</param>
/// <param name="snapshot">All read argument files and missing higher-precedence configuration candidates.</param>
public sealed record NativeArgumentExpansionResult(
    ImmutableArray<string> arguments,
    ImmutableArray<string> linkerOnlyArguments,
    SourceSnapshot snapshot
)
{
    /// <summary>Gets reasons why unclassified external driver effects prevent complete generation cache admission.</summary>
    public ImmutableArray<string> cacheIneligibilityReasons { get; init; } = [];
}

/// <summary>Captures and expands explicitly resolved response and Clang configuration inputs before a tool or parser runs.</summary>
public sealed class NativeArgumentInputSnapshotBuilder
{
    private readonly IHostPathService m_paths;

    /// <summary>Creates a Clang argument interpreter bound to explicit host path semantics.</summary>
    /// <param name="paths">The session-owned host path identity service.</param>
    public NativeArgumentInputSnapshotBuilder(IHostPathService paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        m_paths = paths;
    }
    /// <summary>Locks argument-file dependencies without changing the caller's execution argument sequence.</summary>
    /// <param name="arguments">The actual driver argument sequence.</param>
    /// <param name="pathBase">The captured absolute native invocation directory.</param>
    /// <param name="windowsResponseSyntax">Whether ordinary response files use Windows quoting.</param>
    /// <param name="clangConfigurations">Whether explicit Clang configuration files are processed.</param>
    /// <param name="compilerPath">The exact compiler used to resolve explicit short configuration names.</param>
    /// <param name="cancellationToken">Cancels bounded input reads and hashes.</param>
    /// <returns>The complete immutable argument-file dependency snapshot.</returns>
    public SourceSnapshot Capture(
        IEnumerable<string> arguments,
        string pathBase,
        bool windowsResponseSyntax = false,
        bool clangConfigurations = true,
        string? compilerPath = null,
        CancellationToken cancellationToken = default
    )
        => Expand(arguments, pathBase, windowsResponseSyntax, clangConfigurations, compilerPath, cancellationToken).snapshot;

    /// <summary>Expands response and explicit configuration files while preserving actual driver precedence and input origins.</summary>
    /// <param name="arguments">The actual user-supplied driver argument sequence.</param>
    /// <param name="pathBase">The captured absolute invocation directory, including ordinary nested response resolution.</param>
    /// <param name="windowsResponseSyntax">Whether ordinary response files use Windows quoting; explicit rsp-quoting overrides it.</param>
    /// <param name="clangConfigurations">Whether explicit Clang configs and their linker-only tail options are understood.</param>
    /// <param name="compilerPath">The pinned compiler for explicit short config lookup; expanded files remove worker executable lookup ambiguity.</param>
    /// <param name="cancellationToken">Cancels bounded recursive reads and snapshot verification.</param>
    /// <param name="snapshotExcludedDirectories">Owned output roots omitted only from raw search-directory membership.</param>
    /// <param name="includeSearchDirectories">Resolved header roots used to locate explicit preincluded header inputs.</param>
    /// <param name="sysRoot">The explicitly resolved target sysroot for sysroot-relative search arguments.</param>
    /// <returns>Compile and linker-tail arguments plus their independently verified original input snapshot.</returns>
    public NativeArgumentExpansionResult Expand(
        IEnumerable<string> arguments,
        string pathBase,
        bool windowsResponseSyntax = false,
        bool clangConfigurations = true,
        string? compilerPath = null,
        CancellationToken cancellationToken = default,
        IEnumerable<string>? snapshotExcludedDirectories = null,
        IEnumerable<string>? includeSearchDirectories = null,
        string? sysRoot = null
    ) {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(pathBase);
        if (!Path.IsPathFullyQualified(pathBase)) throw new ArgumentException("Argument input discovery requires an absolute captured path base.", nameof(pathBase));
        Dictionary<string, InputDependency> inputs = new(m_paths.comparer);
        Dictionary<string, MissingInputDependency> missing = new(inputs.Comparer);
        HashSet<string> active = new(inputs.Comparer);
        long total = 0;
        List<string> configured = [], explicitOptions = [], linkerOnly = [];
        string? configUserDirectory = null, configSystemDirectory = null;
        string[] initial = arguments.ToArray();
        if (clangConfigurations)
            foreach (string option in initial)
            {
                if (option == "--rsp-quoting=windows") windowsResponseSyntax = true;
                else if (option == "--rsp-quoting=posix") windowsResponseSyntax = false;
            }
        // Real drivers expand ordinary responses before selecting explicit configuration files.
        List<string> commandLine = ExpandTokens(initial, pathBase, windowsResponseSyntax, 0, config: false, expandConfigs: false);
        configUserDirectory = ConfigDirectory(commandLine, "--config-user-dir");
        configSystemDirectory = ConfigDirectory(commandLine, "--config-system-dir");
        if (clangConfigurations)
        {
            for (int index = 0; index < commandLine.Count; index++)
            {
                string value = commandLine[index], token = Unwrap(value), wrapper = value.StartsWith("/clang:", StringComparison.Ordinal) ? "/clang:" : string.Empty;
                if (token == "--config" || token.StartsWith("--config=", StringComparison.Ordinal))
                {
                    string filename = token == "--config" ? ++index < commandLine.Count ? Unwrap(commandLine[index]) : throw new InvalidDataException("A Clang --config option requires a filename.") : token[9..];
                    configured.AddRange(ReadInput(ResolveConfig(filename), 0, isConfig: true, pathBase, windowsResponseSyntax).Select(item => wrapper + item));
                }
                else explicitOptions.Add(value);
            }
        }
        else explicitOptions.AddRange(commandLine);
        ImmutableArray<string> expanded = configured.Concat(explicitOptions).ToImmutableArray();
        NativeDriverInputClassifier.Result classified = NativeDriverInputClassifier.Capture(expanded.Concat(linkerOnly), pathBase, snapshotExcludedDirectories, includeSearchDirectories, sysRoot, m_paths, cancellationToken);
        SourceSnapshot snapshot = classified.snapshot with
        {
            files = inputs.Values.Concat(classified.snapshot.files).DistinctBy(input => input.path, inputs.Comparer).OrderBy(input => input.path, StringComparer.Ordinal).ToImmutableArray(),
            absentInputs = missing.Values.Concat(classified.snapshot.absentInputs).DistinctBy(input => input.path, inputs.Comparer).OrderBy(input => input.path, StringComparer.Ordinal).ToImmutableArray()
        };
        snapshot = InputPathSemanticsSnapshot.Capture(snapshot, m_paths);
        if (SourceSnapshotVerifier.GetMismatch(snapshot, m_paths, cancellationToken) is string mismatch)
            throw new IOException("Native argument inputs changed while their closure was being captured. " + mismatch);
        return new(expanded, linkerOnly.ToImmutableArray(), snapshot) { cacheIneligibilityReasons = classified.cacheIneligibilityReasons };

        List<string> ExpandTokens(
            IEnumerable<string> tokens,
            string responseBase,
            bool quoting,
            int depth,
            bool config,
            bool expandConfigs
        ) {
            if (depth > 32) throw new InvalidDataException("Native response files exceed the maximum nesting depth.");
            string[] values = tokens.ToArray();
            List<string> result = [];
            for (int index = 0; index < values.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string argument = values[index], token = Unwrap(argument), wrapper = argument.StartsWith("/clang:", StringComparison.Ordinal) ? "/clang:" : string.Empty;
                bool tail = config && token.StartsWith('$');
                if (tail) token = token[1..];
                List<string> current;
                if (token.StartsWith("-Wl,", StringComparison.Ordinal))
                {
                    List<string> forwarded = ExpandTokens(token[4..].Split(','), responseBase, quoting, depth + 1, config: false, expandConfigs: false);
                    current = [];
                    foreach (string item in forwarded) { current.Add("-Xlinker"); current.Add(item); }
                }
                else if (clangConfigurations && expandConfigs && (token == "--config" || token.StartsWith("--config=", StringComparison.Ordinal)))
                {
                    string filename = token == "--config" ? ++index < values.Length ? Unwrap(values[index]) : throw new InvalidDataException("A Clang --config option requires a filename.") : token[9..];
                    current = ReadInput(ResolveConfig(filename), depth, isConfig: true, responseBase, quoting);
                }
                else if (token.StartsWith('@'))
                {
                    if (token.Length == 1) throw new InvalidDataException("A native response-file argument has no filename.");
                    current = ReadInput(Path.GetFullPath(token[1..], responseBase), depth, isConfig: config, responseBase, quoting);
                }
                else current = [token];
                IEnumerable<string> wrapped = current.Select(item => wrapper + item);
                if (tail) linkerOnly.AddRange(wrapped);
                else result.AddRange(wrapped);
            }
            return result;
        }

        List<string> ReadInput(
            string path,
            int depth,
            bool isConfig,
            string responseBase,
            bool quoting
        ) {
            if (!active.Add(path)) throw new InvalidDataException("Native response files contain a recursive inclusion cycle.");
            try
            {
                using FileStream stream = File.OpenRead(path);
                if (stream.Length > 4 * 1024 * 1024 || total + stream.Length > 64 * 1024 * 1024 || inputs.Count >= 4096)
                    throw new InvalidDataException("Native response-file input closure exceeds its size or file-count limit.");
                byte[] bytes = new byte[(int)stream.Length];
                stream.ReadExactly(bytes);
                total += bytes.Length;
                InputDependency captured = new(path, PhysicalPathResolver.Resolve(path, m_paths), bytes.Length, new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(bytes))));
                if (inputs.TryGetValue(path, out InputDependency? previous) && previous != captured) throw new IOException("A native argument file changed during closure discovery.");
                inputs[path] = captured;
                using StreamReader reader = new(new MemoryStream(bytes), new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
                IEnumerable<string> parsed = Tokenize(reader.ReadToEnd(), isConfig ? false : quoting, isConfig);
                if (isConfig) parsed = parsed.Select(token => token.Replace("<CFGDIR>", Path.GetDirectoryName(path)!, StringComparison.Ordinal));
                return ExpandTokens(parsed, isConfig ? Path.GetDirectoryName(path)! : responseBase, isConfig ? false : quoting, depth + 1, isConfig, expandConfigs: isConfig);
            }
            finally { active.Remove(path); }
        }

        string ResolveConfig(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename)) throw new InvalidDataException("A Clang configuration filename must not be empty.");
            if (Path.IsPathFullyQualified(filename) || filename.Contains('/') || filename.Contains('\\')) return Path.GetFullPath(filename, pathBase);
            if (configUserDirectory is null || configSystemDirectory is null || compilerPath is null || !Path.IsPathFullyQualified(compilerPath))
                throw new InvalidDataException("A short Clang --config filename requires explicit --config-user-dir and --config-system-dir overrides plus a resolved compiler; alternatively use an explicit configuration file path.");
            foreach (string directory in new[] { configUserDirectory, configSystemDirectory, Path.GetDirectoryName(compilerPath)! }.Where(value => value.Length != 0).Distinct(inputs.Comparer))
            {
                string path = Path.Combine(directory, filename);
                if (File.Exists(path)) return path;
                if (Directory.Exists(path)) throw new InvalidDataException("A Clang configuration input is a directory instead of a file.");
                missing[path] = new(path, PhysicalPathResolver.Resolve(path, m_paths));
            }
            throw new FileNotFoundException("The explicitly resolved Clang configuration file is missing.", filename);
        }

        string? ConfigDirectory(
            IReadOnlyList<string> values,
            string option
        ) {
            string? result = null;
            for (int index = 0; index < values.Count; index++)
            {
                string token = Unwrap(values[index]);
                if (token.StartsWith(option + "=", StringComparison.Ordinal)) result = token[(option.Length + 1)..];
                else if (token == option) result = ++index < values.Count ? Unwrap(values[index]) : throw new InvalidDataException("A Clang configuration directory option requires a value.");
            }
            return result is null ? null : result.Length == 0 ? string.Empty : Path.GetFullPath(result, pathBase);
        }
    }

    private static string Unwrap(string value) => value.StartsWith("/clang:", StringComparison.Ordinal) ? value[7..] : value;
    private static IEnumerable<string> Tokenize(
        string text,
        bool windowsResponseSyntax,
        bool config = false
    ) {
        List<string> tokens = [];
        StringBuilder current = new();
        char quote = '\0';
        bool started = false;
        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];
            if (character == '\0') throw new InvalidDataException("A native response file contains a NUL character.");
            if (config && character == '#' && quote == '\0' && !started)
            {
                while (index + 1 < text.Length && text[index + 1] is not ('\r' or '\n')) index++;
                continue;
            }
            if (character == '\\')
            {
                if (!windowsResponseSyntax)
                {
                    if (++index >= text.Length) throw new InvalidDataException("A GNU response file ends in an incomplete escape.");
                    if (config && text[index] is '\r' or '\n')
                    {
                        if (text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
                        continue;
                    }
                    current.Append(text[index]); started = true; continue;
                }
                int count = 1;
                while (index + 1 < text.Length && text[index + 1] == '\\') { count++; index++; }
                if (index + 1 < text.Length && text[index + 1] == '"')
                {
                    current.Append('\\', count / 2); index++;
                    if ((count & 1) != 0) current.Append('"');
                    else quote = quote == '\0' ? '"' : '\0';
                }
                else current.Append('\\', count);
                started = true; continue;
            }
            if (character is '"' or '\'' && (!windowsResponseSyntax || character == '"'))
            {
                if (quote == '\0') quote = character;
                else if (quote == character) quote = '\0';
                else current.Append(character);
                started = true; continue;
            }
            if (char.IsWhiteSpace(character) && quote == '\0')
            {
                if (started) { tokens.Add(current.ToString()); current.Clear(); started = false; }
            }
            else { current.Append(character); started = true; }
        }
        if (quote != '\0') throw new InvalidDataException("A native response file contains an unterminated quoted argument.");
        if (started) tokens.Add(current.ToString());
        return tokens;
    }
}
