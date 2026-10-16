using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;
using BGCS.NativeBuild.Validation;
using BGCS.Platforms.Assets;
using BGCS.Platforms.Targeting;

namespace BGCS.NativeBuild.Packaging;

/// <summary>Packages native dynamic RID assets and explicit static native references through one candidate sink.</summary>
public sealed class NativeAssetPackager : INativeArtifactPackager
{
    /// <inheritdoc/>
    public string id => "bgcs.native-assets";
    /// <inheritdoc/>
    public async ValueTask<NativeAssetPackageResult> PackageAsync(
        NativeArtifactPackagingRequest request,
        CancellationToken cancellationToken
    ) {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.sink);
        if (request.artifacts.IsDefaultOrEmpty) throw new ArgumentException("A native asset package requires at least one complete artifact.", nameof(request));
        ImmutableArray<NativePackageAsset>.Builder entries = ImmutableArray.CreateBuilder<NativePackageAsset>();
        List<(NativePackageAsset asset, string path)> writes = [];
        HashSet<string> identities = new(StringComparer.OrdinalIgnoreCase);
        foreach (NativeAssetInput input in request.artifacts.OrderBy(input => input.targetId.value, StringComparer.Ordinal).ThenBy(input => input.path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string filename = Path.GetFileName(input.path);
            if (filename is "" or "." or ".." || filename.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '_' and not '-'))
                throw new ArgumentException("A native package artifact must have a portable filename.");
            var target = input.target ?? new TargetResolver().Resolve(new(input.targetId));
            if (target.targetId != input.targetId) throw new ArgumentException("A native asset target descriptor changed its opaque target identity.");
            NativeArtifactVerifier.Verify(input.path, target, input.outputKind);
            string rid = RuntimeIdentifierMapper.Map(target);
            string? item = input.outputKind == NativeOutputKind.SharedLibrary ? null
                : target.platformId == "emscripten" ? "NativeFileReference"
                : target.platformId is "ios" or "ios-simulator" ? "NativeReference" : "NativeLibrary";
            string relative = item is null ? "runtimes/" + rid + "/native/" + filename : "buildTransitive/native/" + input.targetId.value + "/" + filename;
            if (!identities.Add(relative)) throw new InvalidOperationException("Native package output paths collide: " + relative);
            await using FileStream stream = new(input.path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            ContentDigest digest = new(Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)));
            if (digest != input.digest) throw new IOException("A native package input changed after its producing verification stage: " + input.path);
            NativePackageAsset entry = new(input.targetId, rid, relative, digest, input.outputKind, item) { target = target };
            entries.Add(entry);
            writes.Add((entry, input.path));
        }
        JsonSerializerOptions options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
        string manifest = JsonSerializer.Serialize(new { schemaVersion = 1, assets = entries.ToImmutable() }, options) + "\n";
        string targets = CreateTargets(entries.ToImmutable());
        foreach ((NativePackageAsset asset, string path) in writes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            if (Digest(bytes) != asset.digest) throw new IOException("A native package input changed between validation and candidate emission: " + path);
            request.sink.WriteBytes(asset.relativePath, bytes);
        }
        const string manifestPath = "bgcs.native-assets.json", targetsPath = "buildTransitive/BGCS.NativeAssets.targets";
        request.sink.WriteText(manifestPath, manifest);
        request.sink.WriteText(targetsPath, targets);
        return new(entries.ToImmutable(), [new(manifestPath, "application/json", Digest(Encoding.UTF8.GetBytes(manifest))), new(targetsPath, "application/xml", Digest(Encoding.UTF8.GetBytes(targets)))]);
    }
    private static string CreateTargets(ImmutableArray<NativePackageAsset> assets)
    {
        XElement project = new("Project");
        foreach (NativePackageAsset asset in assets.Where(asset => asset.buildItem is not null))
        {
            string condition = "'$(RuntimeIdentifier)' == '" + asset.runtimeIdentifier + "'";
            if (asset.buildItem == "NativeLibrary") condition += " And '$(PublishAot)' == 'true'";
            XElement item = new(asset.buildItem!, new XAttribute("Include", "$(MSBuildThisFileDirectory)" + asset.relativePath["buildTransitive/".Length..]));
            if (asset.buildItem == "NativeReference") item.Add(new XElement("Kind", "Static"), new XElement("ForceLoad", "true"));
            project.Add(new XElement("ItemGroup", new XAttribute("Condition", condition), item));
        }
        return new XDocument(project).ToString() + "\n";
    }
    private static ContentDigest Digest(ReadOnlySpan<byte> bytes) => new(Convert.ToHexStringLower(SHA256.HashData(bytes)));
}
