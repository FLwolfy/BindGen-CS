using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts;
using BGCS.Contracts.Artifacts;
using BGCS.Infrastructure.Publishing;

namespace BGCS;

/// <summary>Inspects detached binding models through the host's unified generation pipeline.</summary>
public sealed partial class BindingHost
{
    /// <summary>Runs the unified generation pipeline in an independently owned temporary output and exposes detached models.</summary>
    /// <param name="request">The complete grouped request with plugin and input provenance.</param>
    /// <param name="cancellationToken">Cancels inspection and all underlying sessions.</param>
    /// <returns>Native, bridge, interoperability and managed models without modifying formal output.</returns>
    public async Task<InspectionResult> InspectAsync(
        GenerationRequest request,
        CancellationToken cancellationToken = default
    ) {
        GenerationResult result = await GeneratePreviewAsync(request, cancellationToken).ConfigureAwait(false);
        return new(result.success, result.targetModels, result.diagnostics);
    }

    /// <summary>Compares generation content against verified previous output, excluding operational cache evidence, without replacing it.</summary>
    /// <param name="request">The complete grouped request.</param>
    /// <param name="cancellationToken">Cancels generation or comparison.</param>
    /// <returns>Exact logical artifact changes and diagnostics.</returns>
    public async Task<GenerationDiffResult> DiffAsync(
        GenerationRequest request,
        CancellationToken cancellationToken = default
    ) {
        ArgumentNullException.ThrowIfNull(request);
        string output = Path.GetFullPath(request.configuration.output.root, request.baseDirectory);
        return await WithPreviewAsync(request, generated =>
        {
            if (!generated.success) return new GenerationDiffResult(false, output, [], generated.diagnostics);
            ImmutableArray<BindingDiagnostic>.Builder diagnostics = generated.diagnostics.ToBuilder();
            try
            {
                ImmutableArray<ArtifactDescriptor> previous = new ArtifactPublicationReader().ReadVerified(output, request.configuration.library.id, cancellationToken);
                var old = previous.ToDictionary(artifact => artifact.relativePath, StringComparer.Ordinal);
                var current = generated.artifacts.ToDictionary(artifact => artifact.relativePath, StringComparer.Ordinal);
                ImmutableArray<ArtifactChange>.Builder changes = ImmutableArray.CreateBuilder<ArtifactChange>();
                foreach (string path in old.Keys.Union(current.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    old.TryGetValue(path, out ArtifactDescriptor? before);
                    current.TryGetValue(path, out ArtifactDescriptor? after);
                    if (before is null) changes.Add(new(path, ArtifactChangeKind.Added, null, after!.digest));
                    else if (after is null) changes.Add(new(path, ArtifactChangeKind.Removed, before.digest, null));
                    else if (before.digest != after.digest && !OnlyCacheEvidenceChanged(path)) changes.Add(new(path, ArtifactChangeKind.Changed, before.digest, after.digest));
                }
                return new GenerationDiffResult(true, output, changes.ToImmutable(), diagnostics.ToImmutable());

                bool OnlyCacheEvidenceChanged(string path)
                {
                    if (path != "generation.report.json" && !path.EndsWith("/metadata/verification.json", StringComparison.Ordinal)) return false;
                    JsonNode? previousContent = JsonNode.Parse(File.ReadAllText(Path.Combine(output, path)));
                    JsonNode? currentContent = JsonNode.Parse(File.ReadAllText(Path.Combine(generated.outputRoot, path)));
                    if (previousContent is not JsonObject before || currentContent is not JsonObject after)
                    {
                        diagnostics.Add(new(BindingDiagnosticSeverity.Information, MetadataDifference(path, previousContent, currentContent, cancellationToken), "BGCS-DIFF-METADATA"));
                        return false;
                    }
                    before.Remove("cache");
                    after.Remove("cache");
                    RemoveCacheDiagnostics(before);
                    RemoveCacheDiagnostics(after);
                    bool equal = JsonNode.DeepEquals(before, after);
                    if (!equal)
                        diagnostics.Add(new(BindingDiagnosticSeverity.Information, MetadataDifference(path, before, after, cancellationToken), "BGCS-DIFF-METADATA"));
                    return equal;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception failure)
            {
                diagnostics.Add(new(BindingDiagnosticSeverity.Error, failure.Message, "BGCS-DIFF-OUTPUT"));
                return new GenerationDiffResult(false, output, [], diagnostics.ToImmutable());
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private static string MetadataDifference(
        string artifact,
        JsonNode? previous,
        JsonNode? current,
        CancellationToken cancellationToken
    ) {
        List<string> differences = [];
        Compare(previous, current, "$");
        return "Metadata changed in '" + artifact + "' (operational cache evidence excluded):" + Environment.NewLine
            + (differences.Count == 0 ? "$: JSON values are equal but artifact content differs." : string.Join(Environment.NewLine, differences));

        void Compare(
            JsonNode? left,
            JsonNode? right,
            string path
        ) {
            cancellationToken.ThrowIfCancellationRequested();
            if (differences.Count == 12) return;
            if (left is JsonObject oldObject && right is JsonObject newObject)
                foreach (string name in oldObject.Select(property => property.Key).Union(newObject.Select(property => property.Key), StringComparer.Ordinal).Order(StringComparer.Ordinal))
                {
                    if (differences.Count == 12) break;
                    string child = path + "[" + JsonSerializer.Serialize(name) + "]";
                    if (!oldObject.TryGetPropertyValue(name, out JsonNode? oldValue)) Add(child, "<missing>", Short(newObject[name]));
                    else if (!newObject.TryGetPropertyValue(name, out JsonNode? newValue)) Add(child, Short(oldValue), "<missing>");
                    else Compare(oldValue, newValue, child);
                }
            else if (left is JsonArray oldArray && right is JsonArray newArray)
            {
                if (oldArray.Count != newArray.Count) Add(path + ".Length", oldArray.Count.ToString(CultureInfo.InvariantCulture), newArray.Count.ToString(CultureInfo.InvariantCulture));
                for (int index = 0; index < Math.Min(oldArray.Count, newArray.Count) && differences.Count < 12; index++)
                    Compare(oldArray[index], newArray[index], path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]");
            }
            else if (!JsonNode.DeepEquals(left, right)) Add(path, Short(left), Short(right));
        }
        void Add(
            string path,
            string left,
            string right
        ) {
            if (differences.Count < 12) differences.Add(path + ": " + left + " -> " + right);
        }
        static string Short(JsonNode? value)
        {
            if (value is null) return "null";
            if (value is JsonObject properties) return "<object, " + properties.Count.ToString(CultureInfo.InvariantCulture) + " properties>";
            if (value is JsonArray elements) return "<array, " + elements.Count.ToString(CultureInfo.InvariantCulture) + " elements>";
            string text = value is JsonValue scalar && scalar.TryGetValue(out string? content) && content is not null
                ? JsonSerializer.Serialize(content.Length > 180 ? content[..180] + "..." : content) : value.ToJsonString();
            return text.Length <= 180 ? text : text[..177] + "...";
        }
    }

    private Task<GenerationResult> GeneratePreviewAsync(
        GenerationRequest request,
        CancellationToken cancellationToken
    )
        => WithPreviewAsync(request, generated => generated, cancellationToken);

    private async Task<T> WithPreviewAsync<T>(
        GenerationRequest request,
        Func<GenerationResult, T> inspect,
        CancellationToken cancellationToken
    ) {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        string scratch = Path.Combine(Path.GetTempPath(), "bgcs-inspection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            GenerationRequest preview = request.WithPreviewOutput(Path.Combine(scratch, "Generated"));
            GenerationResult generated = await GenerateAsync(preview, cancellationToken).ConfigureAwait(false);
            return inspect(generated);
        }
        finally
        {
            if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
        }
    }

    private static void RemoveCacheDiagnostics(JsonObject document)
    {
        if (document["diagnostics"] is not JsonArray diagnostics) return;
        for (int index = diagnostics.Count - 1; index >= 0; index--)
            if (diagnostics[index] is JsonObject diagnostic && diagnostic["code"]?.GetValue<string>() is string code
                && code is "BGCS-CACHE-UNAVAILABLE" or "BGCS-CACHE-MISS" or "BGCS-CACHE-BUDGET" or "BGCS-CACHE-STORE") diagnostics.RemoveAt(index);
    }
}
