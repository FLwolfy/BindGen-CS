using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace BGCS.Tool.Commands;

/// <summary>Reads resource bytes through PE metadata without loading or executing the delivered assembly.</summary>
internal static class EmbeddedResourceProof
{
    internal static byte[]? Read(
        ZipArchiveEntry assembly,
        string resourceName
    ) {
        try { return ReadCore(assembly, resourceName); }
        catch (Exception exception) when (exception is BadImageFormatException or OverflowException or ArgumentOutOfRangeException)
        { throw new InvalidDataException("The delivered PE embedded-resource metadata is invalid.", exception); }
    }

    private static byte[]? ReadCore(
        ZipArchiveEntry assembly,
        string resourceName
    ) {
        if (assembly.Length is <= 0 or > 64 * 1024 * 1024)
            throw new InvalidDataException("The resource-bearing assembly exceeds its bounded proof input.");
        using Stream input = assembly.Open();
        using MemoryStream bytes = new();
        input.CopyTo(bytes);
        bytes.Position = 0;
        using PEReader pe = new(bytes);
        if (!pe.HasMetadata || pe.PEHeaders.CorHeader is null)
            throw new InvalidDataException("The declared embedded resource owner is not a managed PE assembly.");
        MetadataReader metadata = pe.GetMetadataReader();
        ManifestResource[] matches = metadata.ManifestResources.Select(handle => metadata.GetManifestResource(handle))
            .Where(resource => metadata.GetString(resource.Name) == resourceName).ToArray();
        if (matches.Length == 0) return null;
        if (matches.Length != 1 || !matches[0].Implementation.IsNil)
            throw new InvalidDataException("The embedded resource identity is duplicate or redirects to external bytes.");
        PEMemoryBlock block = pe.GetSectionData(pe.PEHeaders.CorHeader.ResourcesDirectory.RelativeVirtualAddress);
        int resourceSize = pe.PEHeaders.CorHeader.ResourcesDirectory.Size;
        int offset = checked((int)matches[0].Offset);
        if (resourceSize < 4 || resourceSize > block.Length || offset < 0 || offset > resourceSize - 4)
            throw new InvalidDataException("The embedded resource offset is outside the actual PE resource section.");
        BlobReader reader = block.GetReader(offset, resourceSize - offset);
        int length = reader.ReadInt32();
        if (length is <= 0 or > 8 * 1024 * 1024 || length > reader.RemainingBytes)
            throw new InvalidDataException("The embedded resource has an invalid bounded extent.");
        return reader.ReadBytes(length);
    }

    internal static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
