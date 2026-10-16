using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Composition;
using BGCS.Configuration;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.NativeBuild.Packaging;

namespace BGCS;

internal sealed class NativePackageCandidateBuilder
{
    internal sealed record Result(
        ImmutableArray<ArtifactDescriptor> artifacts,
        NativeAssetPackageResult assets
    );

    internal async Task<Result> PrepareAsync(
        BindingConfiguration configuration,
        NativeTargetDescriptor target,
        ArtifactDescriptor nativeArtifact,
        string candidateRoot,
        IArtifactSink sink,
        FrozenExtensionRegistry registry,
        GenerationInputTracker inputTracker,
        CancellationToken cancellationToken
    ) {
        string requested = configuration.nativeBuild.packaging.packager;
        NativePackagerRegistration? registration = requested == "auto"
            ? registry.NativeArtifactPackagers.Length switch
            {
                0 => null,
                1 => registry.NativeArtifactPackagers[0],
                _ => throw new InvalidOperationException("Automatic native packaging requires one unambiguous configured packager.")
            }
            : registry.NativeArtifactPackagers.SingleOrDefault(service => service.Id == requested);
        if (requested is not ("auto" or "bgcs.native-assets") && registration is null)
            throw new InvalidOperationException("The requested native artifact packager is not registered: " + requested);
        INativeArtifactPackager service = registration is null ? new NativeAssetPackager()
            : registration.Factory() ?? throw new InvalidOperationException("A native artifact packager factory returned no service.");
        string binary = Path.GetFullPath(nativeArtifact.relativePath, candidateRoot);
        string prefix = target.targetId.value + "/native/package/";
        TrackingPackageSink output = new(sink, prefix);
        NativeAssetPackageResult result;
        try
        {
            inputTracker.Capture(service, target, cancellationToken);
            CheckIdentity();
            using (FileStream stream = File.OpenRead(binary))
                if (new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(stream))) != nativeArtifact.digest)
                    throw new IOException("The verified native artifact changed before asset preparation.");
            NativeOutputKind kind = configuration.nativeBuild.outputKind ?? BGCS.Platforms.Capabilities.PlatformCapabilityCatalog.Get(target).defaultOutputKind;
            result = await service.PackageAsync(new([new(binary, target.targetId, kind, nativeArtifact.digest) { target = target }], output), cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("A native packager returned no artifact result.");
            CheckIdentity();
            if (result.assets.IsDefaultOrEmpty || result.metadataArtifacts.IsDefault || result.assets.Any(asset => asset is null || asset.targetId != target.targetId
                || asset.outputKind != kind || asset.digest != nativeArtifact.digest || string.IsNullOrWhiteSpace(asset.runtimeIdentifier)
                || asset.target is not null && !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(asset.target), JsonSerializer.SerializeToElement(target)))
                || result.metadataArtifacts.Any(artifact => artifact is null || string.IsNullOrWhiteSpace(artifact.mediaType)))
                throw new InvalidOperationException("A native packager changed the verified artifact identity or returned incomplete asset metadata.");
            ImmutableArray<ArtifactDescriptor> declared = result.assets.Select(asset => new ArtifactDescriptor(asset.relativePath, "application/octet-stream", asset.digest))
                .Concat(result.metadataArtifacts).ToImmutableArray();
            if (declared.Select(artifact => artifact.relativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != declared.Length || declared.Length != output.written.Count)
                throw new InvalidOperationException("Native package assets collide or omit written candidate files.");
            foreach (ArtifactDescriptor artifact in declared)
                if (!output.written.TryGetValue(artifact.relativePath, out ContentDigest digest) || digest != artifact.digest)
                    throw new InvalidOperationException("A native package artifact declaration differs from the bytes written through its candidate sink.");
            return new(declared.Select(artifact => artifact with { relativePath = prefix + artifact.relativePath }).ToImmutableArray(), result);
        }
        finally
        {
            if (service is IAsyncDisposable asynchronous) await asynchronous.DisposeAsync().ConfigureAwait(false);
            else if (service is IDisposable disposable) disposable.Dispose();
        }

        void CheckIdentity()
        {
            if (service.id != (registration?.Id ?? "bgcs.native-assets"))
                throw new InvalidOperationException("A native artifact packager changed its frozen registration identity.");
        }
    }

    private sealed class TrackingPackageSink(
        IArtifactSink inner,
        string prefix
    ) : IArtifactSink
    {
        internal Dictionary<string, ContentDigest> written { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string WriteText(
            string path,
            string content
        ) => WriteBytes(path, new UTF8Encoding(false).GetBytes(content));
        public string WriteBytes(
            string path,
            ReadOnlyMemory<byte> content
        ) {
            if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || path.Contains(':') || Path.IsPathRooted(path)
                || path.Split('/').Any(segment => segment is "" or "." or "..") || !written.TryAdd(path, new(Convert.ToHexStringLower(SHA256.HashData(content.Span)))))
                throw new InvalidOperationException("Native package output paths must be unique portable candidate-relative paths.");
            return inner.WriteBytes(prefix + path, content);
        }
    }
}
