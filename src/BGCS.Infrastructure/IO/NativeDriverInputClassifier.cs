using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;

namespace BGCS.Infrastructure.IO;

internal static class NativeDriverInputClassifier
{
    internal sealed record Result(
        SourceSnapshot snapshot,
        ImmutableArray<string> cacheIneligibilityReasons
    );

    internal static Result Capture(
        IEnumerable<string> arguments,
        string pathBase,
        IEnumerable<string>? excludedDirectories,
        IEnumerable<string>? declaredSearchDirectories,
        string? sysRoot,
        CancellationToken cancellationToken
    ) {
        string[] values = arguments.Select(value => value.StartsWith("/clang:", StringComparison.Ordinal) ? value[7..] : value)
            .Where(value => value is not ("-Xclang" or "-Xlinker" or "-Xassembler")).ToArray();
        StringComparer comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        Dictionary<string, InputDependency> files = new(comparer);
        Dictionary<string, DirectoryInputDependency> directories = new(comparer);
        Dictionary<string, MissingInputDependency> missing = new(comparer);
        List<string> reasons = [], searchRoots = (declaredSearchDirectories ?? []).Select(path => Path.GetFullPath(path, pathBase)).ToList();
        ImmutableArray<string> exclusions = (excludedDirectories ?? []).Select(path => Path.GetFullPath(path, pathBase)).Distinct(comparer).ToImmutableArray();
        List<(string path, bool header)> pendingFiles = [];
        string prefix = string.Empty;
        for (int index = 0; index < values.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string token = values[index];
            if (TakeValue(token, ref index, ["-I", "-isystem", "-iquote", "-idirafter", "-F", "-iframework", "-L", "/I", "/external:I", "/LIBPATH:"], out string? directory))
            {
                string absolute = ResolveDirectory(directory!);
                if (File.Exists(absolute)) throw new IOException("A declared driver search input is a file rather than a directory: " + absolute);
                directories[absolute] = DirectoryMembershipDigest.Capture(absolute, exclusions, cancellationToken);
                if (!token.StartsWith("-L", StringComparison.Ordinal) && !token.StartsWith("/LIBPATH:", StringComparison.Ordinal)) searchRoots.Add(absolute);
                continue;
            }
            if (TakeValue(token, ref index, ["-iprefix"], out string? includePrefix)) { prefix = includePrefix!; continue; }
            if (TakeValue(token, ref index, ["-iwithprefixbefore", "-iwithprefix"], out string? suffix))
            {
                string absolute = ResolveDirectory(prefix + suffix);
                directories[absolute] = DirectoryMembershipDigest.Capture(absolute, exclusions, cancellationToken);
                searchRoots.Add(absolute);
                continue;
            }
            if (TakeValue(token, ref index, ["-include-pch", "-fmodule-map-file", "-fmodule-file", "-ivfsoverlay", "-fprofile-instr-use", "-fprofile-list", "-fsanitize-ignorelist", "-fsanitize-blacklist", "-T", "--script"], out string? file))
            {
                if (token.StartsWith("-fmodule-file", StringComparison.Ordinal) && file!.Contains('=') && !Path.IsPathFullyQualified(file)) file = file[(file.IndexOf('=') + 1)..];
                pendingFiles.Add((file!, false));
                continue;
            }
            if (TakeValue(token, ref index, ["-include", "-imacros", "/FI"], out string? header)) { pendingFiles.Add((header!, true)); continue; }
            if (TakeValue(token, ref index, ["--sysroot", "-isysroot"], out string? selectedRoot))
            {
                string absolute = Path.GetFullPath(selectedRoot!, pathBase);
                directories[absolute] = DirectoryMembershipDigest.Capture(absolute, exclusions, cancellationToken);
                sysRoot = absolute;
                continue;
            }
            if (TakeValue(token, ref index, ["-D", "-U", "-o", "-MF", "-MT", "-MQ", "-x", "--config-user-dir", "--config-system-dir", "--rsp-quoting"], out _)) continue;
            if (KnownScalar(token)) continue;
            if (token.StartsWith('-') || token.StartsWith('/')) reasons.Add("Unclassified driver input effects: " + token);
        }
        foreach ((string input, bool header) in pendingFiles)
        {
            IEnumerable<string> candidates = header && !Path.IsPathFullyQualified(input)
                ? new[] { Path.GetFullPath(input, pathBase) }.Concat(searchRoots.Select(root => Path.GetFullPath(input, root))).Distinct(comparer)
                : [Path.GetFullPath(input, pathBase)];
            string? selected = null;
            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate)) { selected = candidate; break; }
                if (Directory.Exists(candidate)) throw new IOException("A declared compiler file input is a directory: " + candidate);
                missing[candidate] = new(candidate, PhysicalPathResolver.Resolve(candidate));
            }
            if (selected is null) throw new FileNotFoundException("A declared driver file input could not be resolved from its invocation or declared search directories.", input);
            using FileStream stream = File.OpenRead(selected);
            files[selected] = new(selected, PhysicalPathResolver.Resolve(selected), stream.Length, new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(stream))));
        }
        SourceSnapshot snapshot = new(files.Values.OrderBy(input => input.path, StringComparer.Ordinal).ToImmutableArray(), directories.Values.ToImmutableArray())
        { absentInputs = missing.Values.OrderBy(input => input.path, StringComparer.Ordinal).ToImmutableArray(), excludedDirectories = exclusions };
        // Expand is the sole caller. Its final verification checks this complete
        // closure together with the response/config inputs already read before
        // classification. No external operation separates these in-memory merges;
        // verifying here as well would traverse every SDK tree twice at one fence.
        return new(snapshot, reasons.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray());

        string ResolveDirectory(string value)
        {
            if (value.StartsWith('=') || value.StartsWith("$SYSROOT", StringComparison.Ordinal))
            {
                if (sysRoot is null) throw new InvalidDataException("A sysroot-relative driver search path requires an explicitly resolved target sysroot.");
                value = Path.Combine(sysRoot, value.StartsWith('=') ? value[1..].TrimStart('/', '\\') : value["$SYSROOT".Length..].TrimStart('/', '\\'));
            }
            return Path.GetFullPath(value, pathBase);
        }
        bool TakeValue(
            string token,
            ref int index,
            string[] options,
            out string? value
        ) {
            foreach (string option in options)
            {
                if (token == option) { value = ++index < values.Length ? values[index] : throw new InvalidDataException("A driver input option requires a value: " + option); return true; }
                if (token.StartsWith(option + "=", StringComparison.Ordinal)) { value = token[(option.Length + 1)..]; return true; }
                if (option is "-I" or "-F" or "-L" or "/I" or "/external:I" or "/LIBPATH:" or "/FI" or "-D" or "-U"
                    && token.StartsWith(option, StringComparison.Ordinal) && token.Length > option.Length) { value = token[option.Length..]; return true; }
            }
            value = null;
            return false;
        }
    }

    private static bool KnownScalar(string token)
        => token is "--no-default-config" or "-fPIC" or "-fpic" or "-fno-pic" or "-fno-PIC" or "-fsyntax-only" or "-c" or "-M" or "-MD" or "-MMD" or "-MP" or "-shared" or "-r" or "-nostdlib"
            or "-nostdinc" or "-nostdinc++" or "-nostdlibinc" or "-nobuiltininc" or "-fno-exceptions" or "-fexceptions" or "-fno-rtti" or "-frtti"
            or "-fshort-wchar" or "-fshort-enums" or "-fno-short-enums" or "-funsigned-char" or "-fsigned-char" or "-ffreestanding" or "-fno-builtin"
            or "-fms-extensions" or "-fno-ms-extensions" or "-fms-compatibility" or "-fno-ms-compatibility" or "-fdeclspec" or "-fno-declspec"
            or "/nologo" or "/c" or "/MD" or "/MT" or "/MDd" or "/MTd" or "/utf-8" or "/TC" or "/TP" or "/EHsc" or "/Zc:__cplusplus" or "/permissive-"
            || token.StartsWith("-W", StringComparison.Ordinal) || token.StartsWith("-O", StringComparison.Ordinal) || token.StartsWith("-g", StringComparison.Ordinal)
            || token.StartsWith("-std=", StringComparison.Ordinal) || token.StartsWith("-fms-compatibility-version=", StringComparison.Ordinal)
            || token.StartsWith("/D", StringComparison.Ordinal) || token.StartsWith("/U", StringComparison.Ordinal) || token.StartsWith("/std:", StringComparison.Ordinal)
            || token.StartsWith("/W", StringComparison.Ordinal) || token.StartsWith("/external:W", StringComparison.Ordinal);
}
