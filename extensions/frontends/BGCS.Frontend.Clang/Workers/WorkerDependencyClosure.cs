using BGCS.Infrastructure.IO;
using BGCS.Infrastructure.Workers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using BGCS.Core.Artifacts;
using BGCS.Infrastructure.Verification;

namespace BGCS.Frontend.Clang.Workers;

internal static class WorkerDependencyClosure
{
    internal static void Verify(
        JsonElement target,
        WorkerAssetManifest manifest
    ) {
        HashSet<string> artifacts = manifest.artifacts.Select(artifact => ArtifactDigestVerifier.ValidateRelativePath(artifact.relativePath)).ToHashSet(StringComparer.Ordinal);
        HashSet<string> declaredRidAssets = new(StringComparer.Ordinal);
        foreach (JsonProperty library in target.EnumerateObject())
        {
            if (library.Value.TryGetProperty("runtime", out JsonElement runtime))
                foreach (JsonProperty asset in runtime.EnumerateObject())
                {
                    string path = AssetPath(asset.Name);
                    string file = Path.GetFileName(path);
                    if (file != "_._") Require(file);
                }
            if (library.Value.TryGetProperty("resources", out JsonElement resources))
                foreach (JsonProperty asset in resources.EnumerateObject())
                {
                    string path = AssetPath(asset.Name);
                    if (Path.GetFileName(path) == "_._") continue;
                    string locale = asset.Value.GetProperty("locale").GetString() ?? throw new IOException("A worker resource omitted its locale.");
                    if (!Regex.IsMatch(locale, "^[A-Za-z0-9]+(?:-[A-Za-z0-9]+)*$", RegexOptions.CultureInvariant))
                        throw new IOException("A worker resource locale is not a safe relative culture identity.");
                    Require(locale + "/" + Path.GetFileName(path));
                }
            if (library.Value.TryGetProperty("native", out JsonElement native))
                foreach (JsonProperty asset in native.EnumerateObject()) NativeAsset(asset.Name, ParserPackage(library.Name));
            if (library.Value.TryGetProperty("runtimeTargets", out JsonElement runtimeTargets))
                foreach (JsonProperty asset in runtimeTargets.EnumerateObject())
                {
                    string path = AssetPath(asset.Name);
                    if (Path.GetFileName(path) == "_._") continue;
                    string? kind = asset.Value.GetProperty("assetType").GetString();
                    string? rid = asset.Value.GetProperty("rid").GetString();
                    if (string.IsNullOrWhiteSpace(rid) || !Regex.IsMatch(rid, "^[A-Za-z0-9]+(?:[.-][A-Za-z0-9]+)*$", RegexOptions.CultureInvariant)
                        || !path.StartsWith("runtimes/" + rid + "/", StringComparison.Ordinal))
                        throw new IOException("A worker runtime asset has an inconsistent RID or relative path.");
                    if (kind == "native") NativeAsset(path, ParserPackage(library.Name));
                    else if (kind == "runtime") { declaredRidAssets.Add(path); Require(path); }
                    else throw new IOException("A worker runtime target declares an unsupported execution asset kind.");
                }
        }
        string parserPrefix = "runtimes/" + manifest.hostRid + "/native/";
        foreach (string path in artifacts.Where(path => path.StartsWith("runtimes/", StringComparison.Ordinal)))
            if (!path.StartsWith(parserPrefix, StringComparison.Ordinal) && !declaredRidAssets.Contains(path))
                throw new IOException("An undeclared runtime asset is present in the locked worker execution closure: " + path);

        void NativeAsset(
            string name,
            bool parserPackage
        ) {
            string path = AssetPath(name);
            if (Path.GetFileName(path) == "_._") return;
            // Parser native dependencies are selected and checked independently
            // against the exact generator host manifest, never a target RID.
            if (parserPackage) return;
            declaredRidAssets.Add(path);
            Require(path);
        }
        void Require(string path)
        {
            if (!artifacts.Contains(path)) throw new IOException("Worker execution dependency is absent from the locked closure: " + path);
        }
    }

    private static string AssetPath(string path) => ArtifactDigestVerifier.ValidateRelativePath(path.Replace('\\', '/'));

    private static bool ParserPackage(string name) => name.StartsWith("libclang.runtime.", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("libClangSharp.runtime.", StringComparison.OrdinalIgnoreCase);
}
