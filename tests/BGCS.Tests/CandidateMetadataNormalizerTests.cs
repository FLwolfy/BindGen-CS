using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core.Inputs;
using Xunit;

namespace BGCS.Tests;

public sealed class CandidateMetadataNormalizerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublishedMetadataAndPreviewUseLogicalPathsAcrossCandidateLifetimes(bool singleFile)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-metadata-位 space-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "native.hpp"), "struct Point { int x; int y; }; inline int total(Point p) { return p.x + p.y; }");
            BindingConfiguration configuration = new()
            {
                schemaVersion = 2,
                library = new() { id = "metadata_paths", nativeName = "metadata_paths" },
                source = new() { language = SourceLanguage.Cpp, headers = ["native.hpp"], parseMacros = false },
                api = new() { @namespace = "Generated.MetadataPaths", name = "MetadataApi" },
                output = new() { layout = singleFile ? "singleFile" : "split" }
            };
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(TestWorkerRoot()).Build();
            GenerationRequest request = new(configuration, root);
            GenerationResult first = await host.GenerateAsync(request);
            Assert.True(first.success, first.GetDiagnosticSummary());
            string[] metadata = Metadata(first);
            string[] stable = StableMetadata(first);
            Assert.NotEmpty(metadata);
            foreach (string json in metadata)
            {
                using JsonDocument document = JsonDocument.Parse(json);
                Assert.DoesNotContain(".bgcs-staging", json, StringComparison.Ordinal);
                Assert.DoesNotContain(".bgcs-preview", json, StringComparison.Ordinal);
            }
            var manifest = Assert.Single(first.nativeSources);
            Assert.NotEmpty(manifest.sources);
            Assert.All(manifest.headerReferences, include => Assert.False(Path.IsPathFullyQualified(include.include)));
            GenerationDiffResult preview = await host.DiffAsync(request);
            Assert.True(preview.success);
            Assert.False(preview.hasChanges, string.Join(", ", preview.changes.Select(change => change.relativePath))
                + Environment.NewLine + string.Join(Environment.NewLine, preview.diagnostics.Select(diagnostic => diagnostic.message)));
            Assert.Equal(metadata, Metadata(first));
            GenerationResult repeated = await host.GenerateAsync(request);
            Assert.True(repeated.success, repeated.GetDiagnosticSummary());
            // Cache receipts are operational observations; the ABI and source manifests remain stable.
            Assert.Equal(stable, StableMetadata(repeated));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task CancellationDoesNotReplaceTheLastCompleteMetadataSet()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-metadata-cancel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "native.h"), "int add(int left, int right);");
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(TestWorkerRoot()).Build();
            GenerationRequest request = new(new()
            {
                schemaVersion = 2,
                library = new() { id = "metadata_cancel", nativeName = "metadata_cancel" },
                source = new() { headers = ["native.h"], parseMacros = false }
            }, root);
            GenerationResult first = await host.GenerateAsync(request);
            Assert.True(first.success, first.GetDiagnosticSummary());
            string[] previous = Metadata(first);
            using CancellationTokenSource cancel = new(); cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.GenerateAsync(request, cancel.Token));
            Assert.Equal(previous, Metadata(first));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string[] Metadata(GenerationResult result) => result.artifacts.Where(artifact => artifact.mediaType == "application/json")
        .OrderBy(artifact => artifact.relativePath, StringComparer.Ordinal).Select(artifact => File.ReadAllText(Path.Combine(result.outputRoot, artifact.relativePath))).ToArray();
    private static string[] StableMetadata(GenerationResult result) => result.artifacts.Where(artifact => artifact.relativePath.EndsWith("abi.manifest.json", StringComparison.Ordinal)
        || artifact.relativePath.EndsWith("native-source.manifest.json", StringComparison.Ordinal)).OrderBy(artifact => artifact.relativePath, StringComparer.Ordinal)
        .Select(artifact => File.ReadAllText(Path.Combine(result.outputRoot, artifact.relativePath))).ToArray();
    private static string TestWorkerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(directory!.FullName, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
