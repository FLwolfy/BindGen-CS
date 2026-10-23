using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Core.Targets;

namespace BGCS.Extension.FreeBsd;

/// <summary>Resolves FreeBSD targets from explicit sysroot/header requirements without using another Unix SDK.</summary>
public sealed class FreeBsdTargetProvider : INativeTargetProvider
{
    /// <inheritdoc/>
    public bool TryResolve(
        NativeTargetRequest request,
        [NotNullWhen(true)] out NativeTargetDescriptor? target
    ) {
        ArgumentNullException.ThrowIfNull(request);
        target = null;
        string[] parts = request.targetId.value.Split('-');
        if (parts.Length != 3 || parts[0] != "freebsd" || parts[2] != "gnu") return false;
        string? cpu = parts[1] switch { "x64" => "x86_64", "x86" => "i686", "arm64" => "aarch64", "arm" => "armv7", _ => null };
        if (cpu is null) return false;
        string expected = cpu + "-unknown-freebsd", triple = request.tripleOverride ?? expected;
        if (!Regex.IsMatch(triple, "\\A" + Regex.Escape(cpu) + "-(?:[A-Za-z0-9_]+-)?freebsd(?:[0-9]+(?:\\.[0-9]+){0,3})?\\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking))
            throw new ArgumentException("The explicit triple does not match the requested FreeBSD architecture/ABI.", nameof(request));
        NativeToolchainDescriptor original = request.toolchain;
        List<string> includes = [.. original.systemIncludeFolders], cxx = [.. original.cxxSystemIncludeFolders];
        if (original.sysRoot is string root)
        {
            root = Path.GetFullPath(root);
            if (includes.Count == 0) { Add(includes, Path.Combine(root, "usr", "local", "include")); Add(includes, Path.Combine(root, "usr", "include")); }
            if (cxx.Count == 0) Add(cxx, Path.Combine(root, "usr", "include", "c++", "v1"));
        }
        NativeToolchainDescriptor selected = new(original.compilerPath, original.sysRoot, includes, original.defines, original.arguments, cxx, original.sdkVersion, original.compilerVersion);
        target = new(request.targetId, "freebsd", parts[1], "gnu", triple, selected, new(true, true), NativeByteOrder.LittleEndian)
        {
            headerSearchPaths = (request.language == SourceLanguage.Cpp ? cxx.Concat(includes) : includes).Distinct(StringComparer.Ordinal).Select(path => new HeaderSearchPath(path, false, false)).ToImmutableArray(),
            sourceConventions = new("__attribute__((visibility(\"default\")))", "", new(parts[1] == "x64" ? "X86_64SysV" : "C", false))
        };
        return true;
    }
    private static void Add(
        List<string> paths,
        string path
    ) { if (Directory.Exists(path)) paths.Add(Path.GetFullPath(path)); }
}
