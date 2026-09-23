using System;
using System.IO;
using System.Text.Json;
using BGCS.Tool.Commands;
using Xunit;

namespace BGCS.Tool.Tests;

public sealed class SupplyChainCommandTests
{
    [Fact]
    public void Run_WritesDeterministicSpdxAndSlsaSubjectsWithArtifactDigests()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-supply-chain-" + Guid.NewGuid().ToString("N"));
        string artifacts = Path.Combine(temp, "packages");
        string outputDirectory = Path.Combine(temp, "attestations");
        Directory.CreateDirectory(artifacts);
        File.WriteAllText(Path.Combine(artifacts, "BGCS.1.0.0.nupkg"), "package");
        try
        {
            using StringWriter output = new();
            using StringWriter error = new();
            int exitCode = SupplyChainCommand.Run(
                [artifacts, "--output", outputDirectory, "--revision", "0123456789abcdef", "--timestamp", "2026-01-02T03:04:05Z"],
                temp, output, error);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, error.ToString());
            using JsonDocument sbom = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputDirectory,
                "sbom.spdx.json")));
            Assert.Equal("SPDX-2.3", sbom.RootElement.GetProperty("spdxVersion").GetString());
            Assert.Equal("BGCS.1.0.0.nupkg",
                sbom.RootElement.GetProperty("packages")[0].GetProperty("name").GetString());
            using JsonDocument provenance = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputDirectory,
                "provenance.slsa.json")));
            Assert.Equal("https://slsa.dev/provenance/v1",
                provenance.RootElement.GetProperty("predicateType").GetString());
            Assert.Equal(64, provenance.RootElement.GetProperty("subject")[0]
                .GetProperty("digest").GetProperty("sha256").GetString()!.Length);
            Assert.Equal("0123456789abcdef", provenance.RootElement.GetProperty("predicate")
                .GetProperty("buildDefinition").GetProperty("resolvedDependencies")[0]
                .GetProperty("digest").GetProperty("gitCommit").GetString());
            string firstSbom = File.ReadAllText(Path.Combine(outputDirectory, "sbom.spdx.json"));
            string firstProvenance = File.ReadAllText(Path.Combine(outputDirectory, "provenance.slsa.json"));
            Assert.Equal(0, SupplyChainCommand.Run(
                [artifacts, "--output", outputDirectory, "--revision", "0123456789abcdef", "--timestamp", "2026-01-02T03:04:05Z"],
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
}
