using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using BGCS.Composition;
using BGCS.Configuration;
using BGCS.Core.Artifacts;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Managed;
using BGCS.Core.Requests;
using BGCS.Infrastructure.Caching;
using BGCS.Infrastructure.IO;

namespace BGCS;

internal static class EmissionStageCache
{
    internal static ContentDigest CreateKey(
        ManagedApiModel model,
        EmissionRequest request,
        BindingConfiguration configuration,
        SourceSnapshot sourceInputs,
        SourceSnapshot declaredNativeInputs,
        GenerationInputTracker inputs,
        FrozenExtensionRegistry definition,
        CancellationToken cancellationToken
    ) {
        Assembly[] closure = [typeof(BindingHost).Assembly, typeof(GenerationStageCache).Assembly, typeof(ManagedApiModel).Assembly,
            typeof(BGCS.Analysis.Interop.InteropModuleVerifier).Assembly, typeof(BGCS.NativeBridge.Planning.NativeBridgePlanner).Assembly,
            typeof(BGCS.CSharp.CSharpBackend).Assembly,
            typeof(Microsoft.CodeAnalysis.SyntaxTree).Assembly, typeof(Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree).Assembly];
        List<object> implementations = [];
        foreach (Assembly assembly in closure.Distinct().OrderBy(assembly => assembly.FullName, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(assembly.Location)) throw new NotSupportedException("Emission caching requires a hashable managed implementation closure.");
            using FileStream stream = File.OpenRead(assembly.Location);
            implementations.Add(new { identity = assembly.FullName, digest = Convert.ToHexStringLower(SHA256.HashData(stream)) });
        }
        JsonElement identity = JsonSerializer.SerializeToElement(new
        {
            stage = "BGCS.CSharpEmission",
            revision = 5,
            implementations,
            configuration,
            modelDigest = ModelDigest(model),
            request = new
            {
                request.libraryName,
                request.importMode,
                request.framework,
                request.managedRuntime,
                request.staticExportTable,
                request.runtimeNamespace,
                request.includeRuntimeSource,
                request.additionalRuntimeSourceFeatures,
                request.singleFile,
                request.singleFileName,
                request.maximumValueStringCodeUnits,
                request.additionalUsings,
                managedProjection = request.managedProjection is null ? null : new
                {
                    assemblies = request.managedProjection.assemblies.Select(input => new
                    { input.path, input.assemblyIdentity, input.digest, length = input.image.Length, input.isFramework }).ToArray(),
                    sources = request.managedProjection.sources.Select(input => new { input.path, input.digest, length = input.image.Length }).ToArray(),
                    request.managedProjection.defines
                }
            },
            // The independently verified model and content/membership digests define
            // this stage. Session-specific owned candidate exclusions are operational
            // paths and must not make otherwise identical emission keys unique.
            inputs.fingerprints,
            sourceInputs = new { sourceInputs.files, sourceInputs.searchDirectories, sourceInputs.absentInputs, pathSemantics = InputPathSemanticsSnapshot.ForInputIdentity(sourceInputs) },
            declaredNativeInputs = new { declaredNativeInputs.files, declaredNativeInputs.searchDirectories, declaredNativeInputs.absentInputs, pathSemantics = InputPathSemanticsSnapshot.ForInputIdentity(declaredNativeInputs) },
            extensionInputs = inputs.snapshots.Select(snapshot => new
            { snapshot.files, snapshot.searchDirectories, snapshot.absentInputs, pathSemantics = InputPathSemanticsSnapshot.ForInputIdentity(snapshot) }).ToArray(),
            plugins = definition.Plugins.Select(plugin => new
            {
                plugin.Descriptor,
                configuration = plugin.Configuration is { } settings ? new
                { settings.id, settings.settings, settings.defaultPathBase, settings.valuePathBases } : null,
                plugin.Manifest
            }).ToArray(),
            registrations = new
            {
                nativeTransforms = definition.NativeTransforms.Select(item => new { item.Id, item.Priority }).ToArray(),
                interopTransforms = definition.InteropTransforms.Select(item => new { item.Id, item.Priority }).ToArray(),
                managedTransforms = definition.ManagedTransforms.Select(item => new { item.Id, item.Priority }).ToArray(),
                typeLowerings = definition.NativeTypes.Select(item => new { item.Id, item.Priority }).ToArray(),
                callableLowerings = definition.NativeCallables.Select(item => new { item.Id, item.Priority }).ToArray(),
                processors = definition.Processors.Select(item => new { item.Id, item.Priority }).ToArray(),
                targets = definition.Targets.Select(item => item.Id).ToArray()
            }
        });
        return CanonicalDigest(identity);
    }

    private static ContentDigest ModelDigest(ManagedApiModel model)
    {
        // This closed, ordered contract graph needs no dictionary canonicalization.
        // Free-form configuration and extension JSON still use CanonicalDigest.
        using SHA256 hash = SHA256.Create();
        using CryptoStream bytes = new(Stream.Null, hash, CryptoStreamMode.Write);
        using (Utf8JsonWriter writer = new(bytes, new() { MaxDepth = 64 }))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("model");
            JsonSerializer.Serialize(writer, model);
            writer.WriteEndObject();
        }
        bytes.FlushFinalBlock();
        return new(Convert.ToHexStringLower(hash.Hash!));
    }

    private static ContentDigest CanonicalDigest(JsonElement identity)
    {
        // Hash the exact canonical byte sequence as it is written, rather than
        // retaining another complete copy of a potentially large model.
        using SHA256 hash = SHA256.Create();
        using CryptoStream bytes = new(Stream.Null, hash, CryptoStreamMode.Write);
        using (Utf8JsonWriter writer = new(bytes)) Canonical(identity, writer);
        bytes.FlushFinalBlock();
        return new(Convert.ToHexStringLower(hash.Hash!));
    }

    private static void Canonical(
        JsonElement element,
        Utf8JsonWriter writer
    ) {
        // Buffers belong to this invocation and recursion depth. Sibling
        // objects reuse them, without retaining another object's property
        // values or allocating LINQ sorting arrays for every model node.
        List<List<(string Name, JsonElement Value, int Ordinal)>> levels = [];
        Write(element, 0);

        void Write(
            JsonElement current,
            int depth
        ) {
            if (current.ValueKind == JsonValueKind.Object)
            {
                while (levels.Count <= depth) levels.Add([]);
                List<(string Name, JsonElement Value, int Ordinal)> properties = levels[depth];
                properties.Clear();
                foreach (JsonProperty property in current.EnumerateObject())
                {
                    properties.Add((property.Name, property.Value, properties.Count));
                }
                properties.Sort(static (
                    left,
                    right
                ) => {
                    int name = StringComparer.Ordinal.Compare(left.Name, right.Name);
                    // OrderBy was stable: duplicate JSON property names must
                    // preserve their original relative order as well.
                    return name != 0 ? name : left.Ordinal.CompareTo(right.Ordinal);
                });
                writer.WriteStartObject();
                foreach (var property in properties)
                {
                    writer.WritePropertyName(property.Name);
                    Write(property.Value, depth + 1);
                }
                writer.WriteEndObject();
                properties.Clear();
            }
            else if (current.ValueKind == JsonValueKind.Array)
            {
                writer.WriteStartArray();
                foreach (JsonElement item in current.EnumerateArray()) Write(item, depth + 1);
                writer.WriteEndArray();
            }
            else current.WriteTo(writer);
        }
    }

    internal sealed class CapturingSink(
        IArtifactSink destination,
        int maximumBytes
    ) : IArtifactSink
    {
        private readonly Dictionary<string, byte[]> m_contents = new(StringComparer.OrdinalIgnoreCase);
        private long m_size;
        private bool m_oversized;
        public string WriteText(
            string relativePath,
            string content
        ) => WriteBytes(relativePath, new UTF8Encoding(false).GetBytes(content));
        public string WriteBytes(
            string relativePath,
            ReadOnlyMemory<byte> content
        ) {
            string path = destination.WriteBytes(relativePath, content);
            m_size = checked(m_size + content.Length);
            if (m_size > maximumBytes) { m_oversized = true; m_contents.Clear(); }
            if (!m_oversized && !m_contents.TryAdd(relativePath, content.ToArray())) throw new InvalidDataException("An emission stage claimed one candidate path twice.");
            return path;
        }
        internal ImmutableArray<CachedStageArtifact> Complete(ImmutableArray<ArtifactDescriptor> artifacts)
        {
            if (m_oversized) return [];
            if (artifacts.IsDefault || artifacts.Length != m_contents.Count) throw new InvalidDataException("The emission stage did not describe its complete captured artifact set.");
            ImmutableArray<CachedStageArtifact>.Builder result = ImmutableArray.CreateBuilder<CachedStageArtifact>();
            foreach (ArtifactDescriptor artifact in artifacts)
            {
                if (!m_contents.Remove(artifact.relativePath, out byte[]? content)
                    || Convert.ToHexStringLower(SHA256.HashData(content)) != artifact.digest.value)
                    throw new InvalidDataException("The emission artifact descriptors disagree with the actual written candidate bytes.");
                result.Add(new(artifact, content));
            }
            return result.ToImmutable();
        }
    }

}
