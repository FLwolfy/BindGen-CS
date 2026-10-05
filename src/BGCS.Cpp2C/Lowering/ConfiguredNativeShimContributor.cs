using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BGCS.Core.Extensibility;

namespace BGCS.Cpp2C.Lowering;

internal sealed class ConfiguredNativeShimContributor(
    CppNativeShim shim,
    string configDirectory
) : ICppArtifactContributor, ICacheFingerprintProvider
{
    public string name => "shim." + shim.name;
    public int priority => 0;

    public IReadOnlyList<CppGeneratedArtifact> Contribute(CppArtifactContext context)
    {
        IEnumerable<string> files = context.stage switch
        {
            CppGeneratedArtifactKind.PublicHeader => shim.publicHeaders,
            CppGeneratedArtifactKind.NativeSource => shim.sourceFiles,
            _ => []
        };
        CppGeneratedArtifactKind kind = context.stage;
        return files.Select(path =>
        {
            string fullPath = Path.GetFullPath(path, configDirectory);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"Configured native shim file not found: {fullPath}", fullPath);
            return new CppGeneratedArtifact(Path.Combine(Sanitize(shim.name), Path.GetFileName(fullPath)), File.ReadAllText(fullPath), kind, exposeToBindings: kind == CppGeneratedArtifactKind.PublicHeader, safety: shim.safety);
        }).ToArray();
    }

    public string GetCacheFingerprint()
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string path in shim.publicHeaders.Concat(shim.sourceFiles).OrderBy(value => value, StringComparer.Ordinal))
        {
            string fullPath = Path.GetFullPath(path, configDirectory);
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes(path));
            if (File.Exists(fullPath))
                hash.AppendData(File.ReadAllBytes(fullPath));
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string Sanitize(string value) => string.Concat(value.Select(character => char.IsLetterOrDigit(character) || character is '_' or '-' ? character : '_'));
}
