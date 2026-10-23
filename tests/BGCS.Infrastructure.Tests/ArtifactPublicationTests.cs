using BGCS.Core.Hosting;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BGCS.Core.Artifacts;
using BGCS.Core.Common;
using BGCS.Infrastructure.IO;
using BGCS.Infrastructure.Publishing;
using BGCS.Infrastructure.Verification;
using Xunit;

namespace BGCS.Infrastructure.Tests;

public sealed class ArtifactPublicationTests
{
    private readonly IHostPathService m_paths = HostTestPaths.Create();
    [Fact]
    public void OwnedClear_PreservesValidOwnershipAndAllowsACompleteRebuild()
    {
        using Scratch scratch = new();
        Assert.False(ArtifactPublisher.ClearOwned(scratch.output, "example", m_paths));
        Publish(scratch.output, "Types/value.g.cs", "previous");
        Assert.True(ArtifactPublisher.ClearOwned(scratch.output, "example", m_paths));
        Assert.Equal(".bgcs-artifacts.json", Path.GetFileName(Assert.Single(Directory.GetFiles(scratch.output, "*", SearchOption.AllDirectories))));
        new ArtifactDigestVerifier(m_paths).Verify(scratch.output, [], [".bgcs-artifacts.json"]);
        Publish(scratch.output, "Types/rebuilt.g.cs", "rebuilt");
        Assert.Equal("rebuilt", File.ReadAllText(Path.Combine(scratch.output, "Types/rebuilt.g.cs")));
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("unlisted")]
    [InlineData("tampered")]
    public void OwnedClear_RejectsUnverifiedDataWithoutDeletingAnyFile(string failure)
    {
        using Scratch scratch = new();
        Publish(scratch.output, "Bindings.cs", "previous");
        if (failure == "unlisted") File.WriteAllText(Path.Combine(scratch.output, "user.txt"), "user data");
        if (failure == "tampered") File.AppendAllText(Path.Combine(scratch.output, "Bindings.cs"), "changed");
        var previous = Directory.GetFiles(scratch.output, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        Assert.Throws<IOException>(() => ArtifactPublisher.ClearOwned(scratch.output, failure == "owner" ? "different-owner" : "example", m_paths));
        Assert.Equal(previous.Keys.Order(), Directory.GetFiles(scratch.output, "*", SearchOption.AllDirectories).Order());
        foreach (var file in previous) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
    }

    [Fact]
    public void IdenticalCompleteCandidate_PreservesExistingFilesAndTheirTimestamps()
    {
        using Scratch scratch = new();
        Publish(scratch.output, "Types/value.g.cs", "unchanged");
        string file = Path.Combine(scratch.output, "Types/value.g.cs");
        DateTime previous = new(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(file, previous);
        DateTime ownership = File.GetLastWriteTimeUtc(Path.Combine(scratch.output, ".bgcs-artifacts.json"));
        Publish(scratch.output, "Types/value.g.cs", "unchanged");
        Assert.Equal(previous, File.GetLastWriteTimeUtc(file));
        Assert.Equal(ownership, File.GetLastWriteTimeUtc(Path.Combine(scratch.output, ".bgcs-artifacts.json")));
        Assert.Single(Directory.GetDirectories(scratch.root));
    }

    [Fact]
    public void ChangedMetadata_PreservesUnchangedManagedArtifactTimestamps()
    {
        using Scratch scratch = new();
        using (ArtifactPublisher publisher = new(scratch.output, "example", m_paths))
        {
            DirectoryArtifactSink sink = new(publisher.candidateRoot, m_paths);
            sink.WriteText("Bindings.cs", "unchanged");
            sink.WriteText("metadata.json", "previous");
            publisher.Commit([Descriptor("Bindings.cs", "unchanged"), Descriptor("metadata.json", "previous")]);
        }
        string managed = Path.Combine(scratch.output, "Bindings.cs");
        DateTime previous = new(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(managed, previous);
        using (ArtifactPublisher publisher = new(scratch.output, "example", m_paths))
        {
            DirectoryArtifactSink sink = new(publisher.candidateRoot, m_paths);
            sink.WriteText("Bindings.cs", "unchanged");
            sink.WriteText("metadata.json", "updated");
            publisher.Commit([Descriptor("Bindings.cs", "unchanged"), Descriptor("metadata.json", "updated")]);
        }
        Assert.Equal(previous, File.GetLastWriteTimeUtc(managed));
        Assert.Equal("updated", File.ReadAllText(Path.Combine(scratch.output, "metadata.json")));
    }

    [Fact]
    public void CompletePublication_RemovesPreviousOwnedArtifactsAfterValidation()
    {
        using Scratch scratch = new();
        Publish(scratch.output, "old.g.cs", "old");
        Publish(scratch.output, "Types/new.g.cs", "new");
        Assert.False(File.Exists(Path.Combine(scratch.output, "old.g.cs")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(scratch.output, "Types/new.g.cs")));
        Assert.Single(Directory.GetDirectories(scratch.root));
        Assert.DoesNotContain(Directory.GetFiles(scratch.root), path => path.EndsWith(".json", StringComparison.Ordinal));
    }

    [Fact]
    public void CorruptCandidate_LeavesTheEntirePreviousOutputUntouched()
    {
        using Scratch scratch = new();
        Publish(scratch.output, "old.g.cs", "previous");
        using (ArtifactPublisher publisher = new(scratch.output, "example", m_paths))
        {
            new DirectoryArtifactSink(publisher.candidateRoot, m_paths).WriteText("new.g.cs", "modified");
            Assert.Throws<IOException>(() => publisher.Commit([Descriptor("new.g.cs", "expected")]));
        }
        Assert.Equal("previous", File.ReadAllText(Path.Combine(scratch.output, "old.g.cs")));
        Assert.False(File.Exists(Path.Combine(scratch.output, "new.g.cs")));
        Assert.Single(Directory.GetDirectories(scratch.root));
    }

    [Fact]
    public void UnownedOrManuallyModifiedOutput_IsNeverReplaced()
    {
        using Scratch scratch = new();
        Directory.CreateDirectory(scratch.output);
        File.WriteAllText(Path.Combine(scratch.output, "notes.txt"), "user file");
        Assert.Throws<IOException>(() => new ArtifactPublisher(scratch.output, "example", m_paths));
        Assert.Equal("user file", File.ReadAllText(Path.Combine(scratch.output, "notes.txt")));
        Directory.Delete(scratch.output, true);
        Publish(scratch.output, "generated.cs", "generated");
        File.WriteAllText(Path.Combine(scratch.output, "notes.txt"), "user file");
        Assert.Throws<IOException>(() => new ArtifactPublisher(scratch.output, "example", m_paths));
        Assert.Equal("user file", File.ReadAllText(Path.Combine(scratch.output, "notes.txt")));
    }

    [Fact]
    public void SourceOverlapAndDifferentLibraryOwnership_AreRejected()
    {
        using Scratch scratch = new();
        Assert.Throws<IOException>(() => new ArtifactPublisher(scratch.output, "example", m_paths, [scratch.root]));
        Assert.Throws<IOException>(() => new ArtifactPublisher(scratch.output, "example", m_paths, [Path.Combine(scratch.output, "source.h")]));
        Publish(scratch.output, "generated.cs", "generated");
        Assert.Throws<IOException>(() => new ArtifactPublisher(scratch.output, "other-library", m_paths));
    }

    [Theory]
    [InlineData("prepared", false)]
    [InlineData("prepared", true)]
    [InlineData("committed", true)]
    public void InterruptedPublication_RecoversTheJournaledCompleteCollection(string state, bool candidateInstalled)
    {
        using Scratch scratch = new();
        Publish(scratch.output, "old.cs", "previous");
        string candidate = Path.Combine(scratch.root, ".bgcs-staging-" + Guid.NewGuid().ToString("N"));
        string backup = Path.Combine(scratch.root, ".bgcs-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(candidate);
        // Recovery records identify the physical owner, while public calls retain the host's logical spelling.
        string physicalOutput = PhysicalPathResolver.Resolve(scratch.output, m_paths);
        candidate = PhysicalPathResolver.Resolve(candidate, m_paths);
        backup = PhysicalPathResolver.Resolve(backup, m_paths);
        ImmutableArray<ArtifactDescriptor> artifacts = [Descriptor("new.cs", "candidate")];
        new DirectoryArtifactSink(candidate, m_paths).WriteText("new.cs", "candidate");
        File.WriteAllText(Path.Combine(candidate, ".bgcs-artifacts.json"), JsonSerializer.Serialize(new { schemaVersion = 1, libraryId = "example", artifacts }));
        Directory.Move(scratch.output, backup);
        if (candidateInstalled) Directory.Move(candidate, scratch.output);
        string identity = m_paths.GetIdentityKey(physicalOutput);
        string journal = Path.Combine(scratch.root, ".bgcs-publication-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity))) + ".json");
        File.WriteAllText(journal, JsonSerializer.Serialize(new { destination = physicalOutput, candidate, backup, libraryId = "example", state, artifacts }));
        using (ArtifactPublisher recovered = new(scratch.output, "example", m_paths))
        {
            string expected = state == "committed" ? "new.cs" : "old.cs";
            Assert.True(File.Exists(Path.Combine(scratch.output, expected)));
            Assert.Equal(state == "committed" ? "candidate" : "previous", File.ReadAllText(Path.Combine(scratch.output, expected)));
            Assert.False(File.Exists(journal));
            Assert.False(Directory.Exists(candidate));
            Assert.False(Directory.Exists(backup));
        }
        Assert.Single(Directory.GetDirectories(scratch.root));
    }

    [Fact]
    public void DigestVerifier_RejectsMissingUnexpectedAndCaseConflictingArtifacts()
    {
        using Scratch scratch = new();
        Directory.CreateDirectory(scratch.output);
        ArtifactDigestVerifier verifier = new(m_paths);
        Assert.Throws<IOException>(() => verifier.Verify(scratch.output, [Descriptor("missing.cs", "value")]));
        File.WriteAllText(Path.Combine(scratch.output, "other.cs"), "value");
        Assert.Throws<IOException>(() => verifier.Verify(scratch.output, []));
        Assert.Throws<IOException>(() => verifier.Verify(scratch.output, [Descriptor("other.cs", "value"), Descriptor("OTHER.cs", "value")]));
        Assert.Throws<IOException>(() => verifier.Verify(scratch.output, [Descriptor("../escape.cs", "value")]));
    }

    private void Publish(string output, string path, string content)
    {
        using ArtifactPublisher publisher = new(output, "example", m_paths);
        new DirectoryArtifactSink(publisher.candidateRoot, m_paths).WriteText(path, content);
        publisher.Commit([Descriptor(path, content)]);
    }

    private static ArtifactDescriptor Descriptor(string path, string text) => new(path, "text/x-csharp", new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))));

    private sealed class Scratch : IDisposable
    {
        internal readonly string root = Path.Combine(Path.GetTempPath(), "bgcs-publication-test-" + Guid.NewGuid().ToString("N"));
        internal string output => Path.Combine(root, "output");
        internal Scratch() => Directory.CreateDirectory(root);
        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
