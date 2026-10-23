using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace BGCS.RepositoryTools.Tests;

public sealed class SupplyChainCommandTests
{
    [Fact]
    public void Run_WritesDeterministicSpdxAndSlsaSubjectsWithArtifactDigests()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-supply-chain-" + Guid.NewGuid().ToString("N"));
        string artifacts = Path.Combine(temp, "packages");
        string outputDirectory = Path.Combine(temp, "attestations");
        string revision = new('a', 40);
        Directory.CreateDirectory(artifacts);
        using (ZipArchive archive = ZipFile.Open(Path.Combine(artifacts, "BGCS.1.0.0.nupkg"), ZipArchiveMode.Create))
        {
            using StreamWriter writer = new(archive.CreateEntry("BGCS.nuspec").Open());
            writer.Write("<package><metadata><id>BGCS</id><version>1.0.0</version><license type=\"expression\">MIT</license></metadata></package>");
        }
        string packagePath = Path.Combine(artifacts, "BGCS.1.0.0.nupkg");
        File.WriteAllText(Path.Combine(artifacts, "package-verification.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            completeProductSet = true,
            sourceRevision = revision,
            checkedOutSourceSha256 = new string('b', 64),
            sourceRevisionStable = true,
            sourceBytesStable = true,
            sourceWorktreeDirty = false,
            sourceAtCompletion = new { sourceRevision = revision, checkedOutSourceSha256 = new string('b', 64), sourceWorktreeDirty = false },
            packages = new[] { new { file = "BGCS.1.0.0.nupkg", bytes = new FileInfo(packagePath).Length,
                sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(packagePath))).ToLowerInvariant() } }
        }));
        try
        {
            using StringWriter output = new();
            using StringWriter error = new();
            int exitCode = Run(
                [artifacts, "--output", outputDirectory, "--revision", revision, "--timestamp", "2026-01-02T03:04:05Z"],
                temp, output, error);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, error.ToString());
            using JsonDocument sbom = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputDirectory,
                "sbom.spdx.json")));
            Assert.Equal("SPDX-2.3", sbom.RootElement.GetProperty("spdxVersion").GetString());
            JsonElement product = Assert.Single(sbom.RootElement.GetProperty("packages").EnumerateArray(), package => package.GetProperty("name").GetString() == "BGCS");
            Assert.Equal("1.0.0", product.GetProperty("versionInfo").GetString());
            Assert.All(sbom.RootElement.GetProperty("packages").EnumerateArray(), package =>
                Assert.DoesNotContain(package.EnumerateObject(), property => property.Value.ValueKind == JsonValueKind.Null));
            using JsonDocument provenance = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputDirectory,
                "provenance.slsa.json")));
            Assert.Equal("https://slsa.dev/provenance/v1",
                provenance.RootElement.GetProperty("predicateType").GetString());
            Assert.Equal(64, provenance.RootElement.GetProperty("subject")[0]
                .GetProperty("digest").GetProperty("sha256").GetString()!.Length);
            Assert.Equal(revision, provenance.RootElement.GetProperty("predicate")
                .GetProperty("buildDefinition").GetProperty("resolvedDependencies")[0]
                .GetProperty("digest").GetProperty("gitCommit").GetString());
            string firstSbom = File.ReadAllText(Path.Combine(outputDirectory, "sbom.spdx.json"));
            string firstProvenance = File.ReadAllText(Path.Combine(outputDirectory, "provenance.slsa.json"));
            Assert.Equal(0, Run(
                [artifacts, "--output", outputDirectory, "--revision", revision, "--timestamp", "2026-01-02T03:04:05Z"],
                temp, TextWriter.Null, TextWriter.Null));
            Assert.Equal(firstSbom, File.ReadAllText(Path.Combine(outputDirectory, "sbom.spdx.json")));
            Assert.Equal(firstProvenance, File.ReadAllText(Path.Combine(outputDirectory, "provenance.slsa.json")));
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }
    private static int Run(
        string[] args,
        string directory,
        TextWriter output,
        TextWriter error
    ) => CliInvocation.Run(["supply-chain", .. args], directory, output, error);
}
