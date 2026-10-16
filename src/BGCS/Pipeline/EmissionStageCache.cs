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
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Managed;
using BGCS.Contracts.Requests;
using BGCS.Infrastructure.Caching;

namespace BGCS;

internal static class EmissionStageCache
{
    internal static ContentDigest CreateKey(
        ManagedApiModel model,
        EmissionRequest request,
        BindingConfiguration configuration,
        GenerationInputTracker inputs,
        FrozenExtensionRegistry definition,
        CancellationToken cancellationToken
    ) {
        Assembly[] closure = [typeof(BindingHost).Assembly, typeof(GenerationStageCache).Assembly, typeof(ManagedApiModel).Assembly,
            typeof(BGCS.Analysis.Interop.InteropModuleVerifier).Assembly, typeof(BGCS.Cpp2C.Planning.CppBridgePlanner).Assembly,
            typeof(BGCS.CSharp.CSharpBackend).Assembly, typeof(BGCS.Runtime.NativeModuleSession).Assembly,
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
            revision = 2,
            implementations,
            configuration,
            model,
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
                managedCompilation = request.managedCompilation is null ? null : new
                {
                    assemblies = request.managedCompilation.assemblies.Select(input => new
                    { input.path, input.assemblyIdentity, input.digest, length = input.image.Length, input.isFramework }).ToArray(),
                    sources = request.managedCompilation.sources.Select(input => new { input.path, input.digest, length = input.image.Length }).ToArray(),
                    request.managedCompilation.defines
                }
            },
            // The independently verified model and content/membership digests define
            // this stage. Session-specific owned candidate exclusions are operational
            // paths and must not make otherwise identical emission keys unique.
            inputs.fingerprints,
            extensionInputs = inputs.snapshots.Select(snapshot => new
            { snapshot.files, snapshot.searchDirectories, snapshot.absentInputs }).ToArray(),
            nativeSourceInputs = inputs.nativeSourceKeys,
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
                typeLowerings = definition.CppTypes.Select(item => new { item.Id, item.Priority }).ToArray(),
                callableLowerings = definition.CppCallables.Select(item => new { item.Id, item.Priority }).ToArray(),
                processors = definition.Processors.Select(item => new { item.Id, item.Priority }).ToArray(),
                targets = definition.Targets.Select(item => item.Id).ToArray()
            }
        });
        using MemoryStream bytes = new();
        using (Utf8JsonWriter writer = new(bytes)) Canonical(identity, writer);
        return new(Convert.ToHexStringLower(SHA256.HashData(bytes.GetBuffer().AsSpan(0, checked((int)bytes.Length)))));
    }

    private static void Canonical(
        JsonElement element,
        Utf8JsonWriter writer
    ) {
        if (element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (JsonProperty property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                Canonical(property.Value, writer);
            }
            writer.WriteEndObject();
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (JsonElement item in element.EnumerateArray()) Canonical(item, writer);
            writer.WriteEndArray();
        }
        else element.WriteTo(writer);
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
