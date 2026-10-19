using System.Collections.Immutable;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using BGCS.Contracts.Artifacts;
using BGCS.Infrastructure.Publishing;
using BGCS.Infrastructure.Verification;

namespace BGCS;

/// <summary>Exposes verified publication reads to integrations through the public SDK boundary.</summary>
public static class BindingArtifactManifest
{
    /// <summary>Reads exact artifact identities under the shared publication lock and verifies ownership and complete content hashes.</summary>
    /// <param name="outputRoot">The formal BGCS output directory.</param>
    /// <param name="libraryId">The expected logical library owner.</param>
    /// <param name="cancellationToken">Cancels waiting for publication ownership.</param>
    /// <returns>The verified complete artifact collection, or empty when no output exists.</returns>
    public static ImmutableArray<ArtifactDescriptor> Read(
        string outputRoot,
        string libraryId,
        CancellationToken cancellationToken = default
    )
        => new ArtifactPublicationReader().ReadVerified(outputRoot, libraryId, cancellationToken);

    /// <summary>Reads typed portable native source requirements from one locked and fully verified publication.</summary>
    /// <param name="outputRoot">The formal BGCS output directory.</param>
    /// <param name="libraryId">The expected logical library owner.</param>
    /// <param name="cancellationToken">Cancels publication locking and content reads.</param>
    /// <returns>All target source manifests, without machine-local input root bindings.</returns>
    public static ImmutableArray<NativeSourceManifest> ReadNativeSources(
        string outputRoot,
        string libraryId,
        CancellationToken cancellationToken = default
    ) => new ArtifactPublicationReader().ReadVerified(outputRoot, libraryId, (
        directory,
        artifacts
    ) => {
        var result = ImmutableArray.CreateBuilder<NativeSourceManifest>();
        JsonSerializerOptions options = new()
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true
        };
        foreach (ArtifactDescriptor artifact in artifacts.Where(artifact => artifact.relativePath.EndsWith("/native/metadata/bridge.manifest.json", StringComparison.Ordinal)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using FileStream stream = File.OpenRead(Path.Combine(directory, artifact.relativePath));
            if (stream.Length > 16 * 1024 * 1024) throw new InvalidDataException("The native source manifest exceeds its size limit.");
            byte[] content = new byte[checked((int)stream.Length)];
            stream.ReadExactly(content);
            if (stream.ReadByte() != -1 || Convert.ToHexStringLower(SHA256.HashData(content)) != artifact.digest.value)
                throw new InvalidDataException("The native source manifest changed during its verified publication read.");
            NativeSourceManifest manifest = JsonSerializer.Deserialize<NativeSourceManifest>(content, options)
                ?? throw new InvalidDataException("The native source manifest is empty.");
            if (artifact.relativePath != manifest.targetId + "/native/metadata/bridge.manifest.json")
                throw new InvalidDataException("The native source manifest target does not match its published path.");
            NativeSourceManifestVerifier.Validate(manifest, artifacts);
            if (result.Any(item => item.targetId == manifest.targetId)) throw new InvalidDataException("The publication repeats a native source manifest target.");
            result.Add(manifest);
        }
        return result.ToImmutable();
    }, cancellationToken);
}
