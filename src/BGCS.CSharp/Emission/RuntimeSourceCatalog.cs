using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace BGCS.CSharp.Emission;

/// <summary>Resolves a versioned runtime source dependency closure without consumer generator dependencies.</summary>
public sealed class RuntimeSourceCatalog
{
    /// <summary>Gets the finite, deterministically ordered source feature identities supported by this implementation.</summary>
    /// <returns>The immutable catalog identities; runtime.complete selects every public Runtime type.</returns>
    public static ImmutableArray<string> GetFeatureIds()
        => ImmutableArray.CreateRange(System.Linq.Enumerable.Order(ReadManifest().Features.Keys, StringComparer.Ordinal));

    /// <summary>Reads and rewrites the exact source closure required by declared runtime features.</summary>
    /// <param name="features">Stable runtime feature identities.</param>
    /// <param name="targetNamespace">The independent runtime source namespace.</param>
    /// <param name="cancellationToken">Cancels catalog traversal and resource reads.</param>
    /// <returns>Deterministically ordered candidate-relative source snapshots.</returns>
    /// <exception cref="InvalidOperationException">A required feature or dependency is missing.</exception>
    public ImmutableArray<(string Path, string Text)> GetSources(
        IEnumerable<string> features,
        string targetNamespace,
        CancellationToken cancellationToken = default
    ) {
        ArgumentNullException.ThrowIfNull(features);
        BGCS.CSharp.Planning.CSharpNames.Namespace(targetNamespace);
        Assembly assembly = typeof(RuntimeSourceCatalog).Assembly;
        Manifest manifest = ReadManifest();
        HashSet<string> selected = new(StringComparer.Ordinal);
        Stack<string> pending = new();
        foreach (string feature in features)
        {
            if (!manifest.Features.TryGetValue(feature, out string[]? roots)) throw new InvalidOperationException($"Runtime feature '{feature}' has no registered source closure.");
            foreach (string file in roots) pending.Push(file);
        }
        while (pending.TryPop(out string? file))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!selected.Add(file)) continue;
            if (!manifest.Files.TryGetValue(file, out string[]? dependencies)) throw new InvalidOperationException($"Runtime source '{file}' has no dependency declaration.");
            foreach (string dependency in dependencies) pending.Push(dependency);
        }
        ImmutableArray<(string Path, string Text)>.Builder result = ImmutableArray.CreateBuilder<(string Path, string Text)>();
        foreach (string file in System.Linq.Enumerable.Order(selected, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using Stream source = assembly.GetManifestResourceStream("BGCS.RuntimeSources." + file) ?? throw new InvalidOperationException($"Runtime source resource '{file}' is missing.");
            using StreamReader reader = new(source, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            result.Add(("Runtime/" + file, RuntimeSourceNamespaceRewriter.Rewrite(reader.ReadToEnd(), targetNamespace)));
        }
        return RuntimeFrameworkQualifier.Qualify(result.ToImmutable(), cancellationToken);
    }

    private static Manifest ReadManifest()
    {
        using Stream manifestStream = typeof(RuntimeSourceCatalog).Assembly.GetManifestResourceStream("BGCS.RuntimeSources.Manifest.json")
            ?? throw new InvalidOperationException("The runtime source catalog manifest is missing.");
        Manifest manifest = JsonSerializer.Deserialize<Manifest>(manifestStream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })
            ?? throw new InvalidOperationException("The runtime source catalog is empty.");
        if (manifest.Revision != 1) throw new InvalidOperationException("The runtime source catalog revision is unsupported.");
        return manifest;
    }

    private sealed record Manifest(
        int Revision,
        Dictionary<string, string[]> Features,
        Dictionary<string, string[]> Files
    );
}
