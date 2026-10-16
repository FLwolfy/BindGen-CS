using System;
using System.Collections.Immutable;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using BGCS.Contracts.Artifacts;

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
        T value
    ) {
        string temporary = path + ".tmp";
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, options);
        try
        {
            using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    internal static T Read<T>(string path)
    {
        using FileStream stream = File.OpenRead(path);
        if (stream.Length > 16 * 1024 * 1024) throw new IOException("A publication manifest exceeds its size limit.");
        return JsonSerializer.Deserialize<T>(stream, options) ?? throw new IOException("A publication manifest is empty.");
    }
}
