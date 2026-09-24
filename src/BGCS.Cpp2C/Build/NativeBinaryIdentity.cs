using System.Buffers.Binary;

namespace BGCS.Cpp2C.Build;

/// <summary>Checks that a staged binary actually has the format and CPU named by its target.</summary>
internal static class NativeBinaryIdentity
{
    public static void Validate(string path, string targetIdentifier)
    {
        string[] target = targetIdentifier.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (target.Length < 2 || target[1] is not ("x64" or "arm64"))
            throw new NotSupportedException($"Binary identity validation does not support target '{targetIdentifier}'.");

        using FileStream stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[4096];
        int length = 0;
        while (length < header.Length)
        {
            int read = stream.Read(header[length..]);
            if (read == 0)
                break;
            length += read;
        }
        ReadOnlySpan<byte> bytes = header[..length];
        bool valid = target[0] switch
        {
            "windows" => IsPe(bytes, target[1]),
            "linux" => IsElf(bytes, target[1]),
            "macos" => IsMachO(bytes, target[1]),
            _ => throw new NotSupportedException($"Binary identity validation does not support target '{targetIdentifier}'.")
        };
        if (!valid)
            throw new InvalidDataException(
                $"Native binary '{path}' does not match target '{targetIdentifier}'. Refusing to stage it under that RID.");
    }

    private static bool IsPe(ReadOnlySpan<byte> bytes, string architecture)
    {
        if (bytes.Length < 0x40 || bytes[0] != 'M' || bytes[1] != 'Z')
            return false;
        int offset = BinaryPrimitives.ReadInt32LittleEndian(bytes[0x3c..]);
        if (offset < 0 || offset > bytes.Length - 6 || !bytes.Slice(offset, 4).SequenceEqual("PE\0\0"u8))
            return false;
        ushort machine = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(offset + 4)..]);
        return machine == (architecture == "x64" ? 0x8664 : 0xaa64);
    }

    private static bool IsElf(ReadOnlySpan<byte> bytes, string architecture)
    {
        if (bytes.Length < 20 || bytes[0] != 0x7f || bytes[1] != 'E' || bytes[2] != 'L' ||
            bytes[3] != 'F' || bytes[4] != 2)
            return false;
        ushort machine = bytes[5] switch
        {
            1 => BinaryPrimitives.ReadUInt16LittleEndian(bytes[18..]),
            2 => BinaryPrimitives.ReadUInt16BigEndian(bytes[18..]),
            _ => (ushort)0
        };
        return machine == (architecture == "x64" ? 62 : 183);
    }

    private static bool IsMachO(ReadOnlySpan<byte> bytes, string architecture)
    {
        if (bytes.Length < 8)
            return false;
        uint magic = BinaryPrimitives.ReadUInt32BigEndian(bytes);
        uint expectedCpu = architecture == "x64" ? 0x01000007u : 0x0100000cu;
        if (magic is 0xfeedfacf or 0xfeedface)
            return BinaryPrimitives.ReadUInt32BigEndian(bytes[4..]) == expectedCpu;
        if (magic is 0xcffaedfe or 0xcefaedfe)
            return BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]) == expectedCpu;

        bool bigEndian = magic is 0xcafebabe or 0xcafebabf;
        bool littleEndian = magic is 0xbebafeca or 0xbfbafeca;
        if (!bigEndian && !littleEndian)
            return false;
        int entrySize = magic is 0xcafebabf or 0xbfbafeca ? 24 : 20;
        uint count = bigEndian
            ? BinaryPrimitives.ReadUInt32BigEndian(bytes[4..])
            : BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]);
        if (count > 100 || bytes.Length < 8 + (int)count * entrySize)
            return false;
        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> entry = bytes[(8 + index * entrySize)..];
            uint cpu = bigEndian
                ? BinaryPrimitives.ReadUInt32BigEndian(entry)
                : BinaryPrimitives.ReadUInt32LittleEndian(entry);
            if (cpu == expectedCpu)
                return true;
        }
        return false;
    }
}
