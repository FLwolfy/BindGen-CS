using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Infrastructure.IO;
using BGCS.Infrastructure.Publishing;
using Xunit;

namespace BGCS.Infrastructure.Tests;

public sealed class ArtifactSetPublisherTests
{
    [Fact]
    public void FileAndDirectoryPublicationPreservesUnrelatedParentFilesAndOwners()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-artifact-set-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string directory = Path.Combine(temporary, "package"), file = Path.Combine(temporary, "native.dll"), unrelated = Path.Combine(temporary, "user.txt");
            File.WriteAllText(unrelated, "user content");
            Directory.CreateDirectory(directory);
            Publish(directory, file, "first");
            Publish(directory, file, "second");
            Assert.Equal("second", File.ReadAllText(file));
            Assert.Equal("second", File.ReadAllText(Path.Combine(directory, "asset.txt")));
            Assert.Equal("user content", File.ReadAllText(unrelated));
            Assert.True(File.Exists(file + ".bgcs-artifact.json"));
            Assert.True(File.Exists(Path.Combine(directory, ".bgcs-artifacts.json")));
            Assert.Empty(Directory.GetFileSystemEntries(temporary, ".bgcs-set-*"));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Fact]
    public void FailedLaterFileInstallRollsBackAlreadyInstalledDirectory()
    {
        if (!OperatingSystem.IsWindows()) return;
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-artifact-set-fault-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string directory = Path.Combine(temporary, "a-package"), file = Path.Combine(temporary, "z-native.dll");
            Publish(directory, file, "previous");
            using ArtifactSetPublisher publisher = new([new(directory, "fixture"), new(file, "fixture", isDirectory: false)]);
            var manifest = Prepare(publisher, directory, file, "candidate");
            using (FileStream locked = new(file, FileMode.Open, FileAccess.Read, FileShare.Read)) Assert.ThrowsAny<IOException>(() => publisher.Commit(manifest));
            Assert.Equal("previous", File.ReadAllText(file));
            Assert.Equal("previous", File.ReadAllText(Path.Combine(directory, "asset.txt")));
            Assert.Empty(Directory.GetFileSystemEntries(temporary, ".bgcs-set-*"));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Fact]
    public void UnownedExistingNamedFileAndOverlappingDestinationsAreRejected()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-artifact-set-owner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string file = Path.Combine(temporary, "native.dll");
            File.WriteAllText(file, "unowned");
            Assert.ThrowsAny<IOException>(() => new ArtifactSetPublisher([new(file, "fixture", isDirectory: false)]));
            Assert.Throws<ArgumentException>(() => new ArtifactSetPublisher([new(temporary, "fixture"), new(file, "fixture", isDirectory: false)]));
            Assert.Equal("unowned", File.ReadAllText(file));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InterruptedPreparedJournalTemporaryIsRecoveredWithoutReplacingPreviousOutput(bool differentCase)
    {
        if (differentCase && !OperatingSystem.IsWindows()) return;
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-set-journal-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string directory = Path.Combine(temporary, "package"), file = Path.Combine(temporary, "native.dll");
            Publish(directory, file, "previous");
            string physicalDirectory = PhysicalPathResolver.Resolve(directory);
            string identity = (OperatingSystem.IsWindows() ? physicalDirectory.ToUpperInvariant() : physicalDirectory) + "\nfixture\nTrue";
            string journal = Path.Combine(temporary, ".bgcs-set-publication-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity))) + ".json");
            string candidate;
            using (ArtifactSetPublisher interrupted = new([new(directory, "fixture")]))
            {
                candidate = interrupted.GetCandidatePath(directory);
                File.WriteAllText(Path.Combine(candidate, "asset.txt"), "candidate", new UTF8Encoding(false));
                var current = ImmutableArray.Create(new ArtifactDescriptor("asset.txt", "text/plain", new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("candidate"))))));
                var recorded = new { schemaVersion = 1, state = "prepared", entries = new[] { new { destination = new ArtifactSetDestination(physicalDirectory, "fixture"), candidate,
                    backup = (string?)null, previous = interrupted.ReadExistingArtifacts(directory), current } } };
                File.WriteAllText(journal, JsonSerializer.Serialize(recorded), new UTF8Encoding(false));
            }
            File.Move(journal, journal + ".tmp");
            using (ArtifactSetPublisher recovered = new([new(differentCase ? directory.ToUpperInvariant() : directory, "fixture")]))
            {
                Assert.Equal("previous", File.ReadAllText(Path.Combine(directory, "asset.txt")));
                Assert.False(Directory.Exists(candidate));
                Assert.False(File.Exists(journal));
                Assert.False(File.Exists(journal + ".tmp"));
            }
            Assert.Equal("previous", File.ReadAllText(file));
            Assert.Empty(Directory.GetFileSystemEntries(temporary, ".bgcs-set-*"));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
    private static void Publish(string directory, string file, string content)
    {
        using ArtifactSetPublisher publisher = new([new(directory, "fixture"), new(file, "fixture", isDirectory: false)]);
        publisher.Commit(Prepare(publisher, directory, file, content));
    }
    private static IReadOnlyDictionary<string, ImmutableArray<ArtifactDescriptor>> Prepare(ArtifactSetPublisher publisher, string directory, string file, string content)
    {
        File.WriteAllText(Path.Combine(publisher.GetCandidatePath(directory), "asset.txt"), content, new UTF8Encoding(false));
        File.WriteAllText(publisher.GetCandidatePath(file), content, new UTF8Encoding(false));
        ContentDigest digest = new(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content))));
        return new Dictionary<string, ImmutableArray<ArtifactDescriptor>>(StringComparer.Ordinal)
        { [directory] = [new("asset.txt", "text/plain", digest)], [file] = [new(Path.GetFileName(file), "application/octet-stream", digest)] };
    }
}
