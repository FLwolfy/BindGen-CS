using BGCS.Core.Hosting;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using BGCS.Core.Artifacts;
using BGCS.Infrastructure.IO;

namespace BGCS.Infrastructure.Publishing;

internal sealed record PublicationJournal(
    string destination,
    string candidate,
    string? backup,
    string libraryId,
    string state,
    ImmutableArray<ArtifactDescriptor> artifacts
);

internal sealed record PublicationOwnership(
    int schemaVersion,
    string libraryId,
    ImmutableArray<ArtifactDescriptor> artifacts
);

internal static class PublicationJournalStorage
{
    internal static readonly JsonSerializerOptions options = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
        MaxDepth = 32
    };

    internal static void Write<T>(
        string path,
        T value,
        IHostPathService paths
    ) {
        OwnedPathBoundary boundary = new(path, paths);
        path = boundary.physicalPath;
        string temporary = path + ".tmp";
        OwnedPathBoundary.RejectPhysicalLinks(temporary);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, options);
        Exception? failure = null;
        bool ownsTemporary = false;
        try
        {
            using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                ownsTemporary = true;
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            boundary.Verify();
            OwnedPathBoundary.RejectPhysicalLinks(temporary);
            File.Move(temporary, path, overwrite: true);
            ownsTemporary = false;
            boundary.Verify();
        }
        catch (Exception error) { failure = error; throw; }
        finally { OwnedFileCleanup.Delete(ownsTemporary ? temporary : null, failure); }
    }

    internal static T Read<T>(
        string path,
        IHostPathService paths
    ) {
        OwnedPathBoundary boundary = new(path, paths);
        using FileStream stream = File.OpenRead(boundary.physicalPath);
        if (stream.Length > 16 * 1024 * 1024) throw new IOException("A publication manifest exceeds its size limit.");
        T value = JsonSerializer.Deserialize<T>(stream, options) ?? throw new IOException("A publication manifest is empty.");
        boundary.Verify();
        return value;
    }
}
