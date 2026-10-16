using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Infrastructure.Publishing;
using Xunit;

namespace BGCS.Infrastructure.Tests;

public sealed class ArtifactSetConcurrencyTests
{
    [Fact]
    public async Task CompetingPublishersInstallOnlyCompleteUniformOwnedSets()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs competing artifact sets " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string directory = Path.Combine(root, "package"), binary = Path.Combine(root, "native.dll");
        await File.WriteAllTextAsync(Path.Combine(root, "NOTICE.txt"), "unrelated user file");
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(45));
        using Barrier start = new(4);
        try
        {
            Task[] publishers = Enumerable.Range(0, 4).Select(worker => Task.Factory.StartNew(() =>
            {
                start.SignalAndWait(deadline.Token);
                for (int revision = 0; revision < 3; revision++)
                {
                    ArtifactSetDestination[] destinations = [new(directory, "fixture"), new(binary, "fixture", isDirectory: false)];
                    using ArtifactSetPublisher publisher = new(worker % 2 == 0 ? destinations : destinations.Reverse(), cancellationToken: deadline.Token);
                    if (!publisher.ReadExistingArtifacts(directory).IsEmpty) CheckPublished(publisher);
                    string token = worker + ":" + revision;
                    string ownerFile = "owner-" + worker + ".bin";
                    byte[] bytes = Encoding.UTF8.GetBytes(token);
                    ContentDigest digest = new(Convert.ToHexStringLower(SHA256.HashData(bytes)));
                    ImmutableArray<ArtifactDescriptor> manifest = [new("api.g.cs", "text/plain", digest), new("metadata.json", "application/json", digest), new(ownerFile, "application/octet-stream", digest)];
                    foreach (ArtifactDescriptor artifact in manifest) File.WriteAllBytes(Path.Combine(publisher.GetCandidatePath(directory), artifact.relativePath), bytes);
                    File.WriteAllBytes(publisher.GetCandidatePath(binary), bytes);
                    publisher.Commit(new Dictionary<string, ImmutableArray<ArtifactDescriptor>>(StringComparer.Ordinal)
                    { [directory] = manifest, [binary] = [new("native.dll", "application/octet-stream", digest)] }, deadline.Token);
                    Assert.Null(publisher.cleanupFailure);
                    CheckPublished(publisher);
                }
            }, deadline.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
            await Task.WhenAll(publishers);
            using (ArtifactSetPublisher reader = new([new(directory, "fixture"), new(binary, "fixture", isDirectory: false)], cancellationToken: deadline.Token))
                CheckPublished(reader);
            Assert.Equal("unrelated user file", await File.ReadAllTextAsync(Path.Combine(root, "NOTICE.txt")));
            Assert.Empty(Directory.GetFileSystemEntries(root, ".bgcs-set-*"));
            Assert.True(File.Exists(Path.Combine(directory, ".bgcs-artifacts.json")));
            Assert.True(File.Exists(binary + ".bgcs-artifact.json"));
        }
        finally { Directory.Delete(root, recursive: true); }

        void CheckPublished(ArtifactSetPublisher publisher)
        {
            ImmutableArray<ArtifactDescriptor> current = publisher.ReadExistingArtifacts(directory);
            Assert.Equal(3, current.Length);
            Assert.Single(publisher.ReadExistingArtifacts(binary));
            string expected = File.ReadAllText(binary);
            Assert.All(current, artifact => Assert.Equal(expected, File.ReadAllText(Path.Combine(directory, artifact.relativePath))));
            Assert.Single(current, artifact => artifact.relativePath.StartsWith("owner-", StringComparison.Ordinal));
            Assert.Equal(current.Select(artifact => artifact.relativePath).Append(".bgcs-artifacts.json").Order(StringComparer.Ordinal),
                Directory.GetFiles(directory).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        }
    }
}
