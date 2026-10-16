using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text;
using BGCS.Contracts.Requests;
using BGCS.NativeBuild.Validation;
using BGCS.Contracts.Targets;
using BGCS.Platforms.Targeting;
using BGCS.Contracts.Native;

namespace BGCS.NativeBuild;

/// <summary>Checks that a staged binary actually has the format and CPU named by its target.</summary>
internal static class NativeBinaryIdentity
{
    public static void Validate(
        string path,
        string targetIdentifier,
        NativeOutputKind outputKind = NativeOutputKind.SharedLibrary
    ) => Validate(path, new TargetResolver().Resolve(new(new(targetIdentifier))), outputKind);

    public static void Validate(
        string path,
        NativeTargetDescriptor target,
        NativeOutputKind outputKind = NativeOutputKind.SharedLibrary
    ) {
        ArgumentNullException.ThrowIfNull(target);
        if (!Enum.IsDefined(outputKind)) throw new ArgumentOutOfRangeException(nameof(outputKind));
        string architecture = target.architectureId;
        string platform = target.platformId == "ios-simulator" ? "ios" : target.platformId;
        if (target.byteOrder == NativeByteOrder.BigEndian && platform is "windows" or "emscripten")
            throw new InvalidDataException("The declared native byte order differs from this binary format's little-endian ABI.");
        if (architecture is not ("x86" or "x64" or "arm" or "arm64" or "wasm32"))
            throw new NotSupportedException($"Binary identity validation does not support architecture '{architecture}' for target '{target.targetId}'.");
        using FileStream stream = File.OpenRead(path);
        if (outputKind == NativeOutputKind.StaticLibrary)
        {
            ValidateArchive(stream, platform, architecture, path, target.byteOrder);
            return;
        }
        if (outputKind == NativeOutputKind.Object && platform == "emscripten")
        {
            if (architecture != "wasm32") throw new InvalidDataException("The Wasm native object target is unsupported.");
            WasmObjectVerifier.Verify(stream, stream.Length);
            return;
        }
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
        bool valid = outputKind == NativeOutputKind.Object ? IsObject(bytes, platform, architecture) : platform switch
        {
            "windows" => IsPe(bytes, architecture),
            "linux" or "android" or "freebsd" => IsElf(bytes, architecture) && ElfType(bytes) == 3,
            "macos" or "ios" => IsMachO(bytes, architecture) && (IsUniversalMachO(bytes)
                ? MachSharedSliceMatches(stream, bytes, architecture, NativeByteOrder.Unknown) : IsMachShared(bytes)),
            "emscripten" => false,
            _ => throw new NotSupportedException($"Binary identity validation does not support platform '{platform}' for target '{target.targetId}'.")
        };
        if (!valid)
            throw new InvalidDataException($"Native binary '{path}' does not match platform/architecture facts of target '{target.targetId}'. Refusing to stage it under that RID.");
        if (target.byteOrder != NativeByteOrder.Unknown)
        {
            bool byteOrderMatches = platform is "macos" or "ios" && IsUniversalMachO(bytes)
                ? MachSharedSliceMatches(stream, bytes, architecture, target.byteOrder)
                : MatchesByteOrder(bytes, platform, target.byteOrder);
            if (!byteOrderMatches) throw new InvalidDataException("The actual native binary byte order differs from the resolved target descriptor: " + path);
        }
    }

    private static bool IsObject(
        ReadOnlySpan<byte> bytes,
        string platform,
        string architecture
    ) => platform switch
    {
        "windows" => IsCoff(bytes, architecture),
        "linux" or "android" or "freebsd" => IsElf(bytes, architecture) && ElfType(bytes) == 1,
        "macos" or "ios" => IsMachO(bytes, architecture) && IsMachObject(bytes),
        _ => false
    };

    private static bool IsCoff(
        ReadOnlySpan<byte> bytes,
        string architecture
    ) {
        if (bytes.Length < 20) return false;
        ushort expected = architecture switch { "x86" => 0x014c, "x64" => 0x8664, "arm" => 0x01c4, "arm64" => 0xaa64, _ => 0 };
        if (expected == 0) return false;
        if (BinaryPrimitives.ReadUInt16LittleEndian(bytes) == expected) return true;
        return bytes.Length >= 56 && BinaryPrimitives.ReadUInt32LittleEndian(bytes) == 0xffff0000
            && BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]) >= 2 && BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]) == expected
            && bytes.Slice(12, 16).SequenceEqual(new byte[] { 0xc7, 0xa1, 0xba, 0xd1, 0xee, 0xba, 0xa9, 0x4b, 0xaf, 0x20, 0xfa, 0xf6, 0x6a, 0xa4, 0xdc, 0xb8 });
    }
    private static bool IsMachObject(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 16) return false;
        uint magic = BinaryPrimitives.ReadUInt32BigEndian(bytes);
        return magic is 0xfeedfacf or 0xfeedface ? BinaryPrimitives.ReadUInt32BigEndian(bytes[12..]) == 1
            : magic is 0xcffaedfe or 0xcefaedfe && BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]) == 1;
    }

    private static ushort ElfType(ReadOnlySpan<byte> bytes) => bytes.Length < 18 ? (ushort)0
        : bytes[5] == 1 ? BinaryPrimitives.ReadUInt16LittleEndian(bytes[16..])
        : bytes[5] == 2 ? BinaryPrimitives.ReadUInt16BigEndian(bytes[16..]) : (ushort)0;

    private static bool IsMachShared(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 16) return false;
        uint magic = BinaryPrimitives.ReadUInt32BigEndian(bytes);
        uint kind = magic is 0xfeedfacf or 0xfeedface ? BinaryPrimitives.ReadUInt32BigEndian(bytes[12..])
            : magic is 0xcffaedfe or 0xcefaedfe ? BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]) : 0;
        return kind is 6 or 8; // MH_DYLIB and MH_BUNDLE are dynamic-library forms; MH_OBJECT is not.
    }

    private static void ValidateArchive(
        FileStream stream,
        string platform,
        string architecture,
        string path,
        NativeByteOrder byteOrder
    ) {
        Span<byte> magic = stackalloc byte[8];
        stream.ReadExactly(magic);
        if (!magic.SequenceEqual("!<arch>\n"u8)) throw new InvalidDataException("Native static assets require a complete archive; thin archives and unknown formats are rejected: " + path);
        Span<byte> header = stackalloc byte[60];
        byte[] buffer = new byte[4096];
        int members = 0;
        while (stream.Position < stream.Length)
        {
            stream.ReadExactly(header);
            if (!header[58..].SequenceEqual("`\n"u8) || !long.TryParse(Encoding.ASCII.GetString(header.Slice(48, 10)).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long size)
                || size < 0 || size > stream.Length - stream.Position) throw new InvalidDataException("A native archive member has an invalid header or size.");
            long end = stream.Position + size;
            string name = Encoding.ASCII.GetString(header[..16]).Trim();
            long nameSize = 0;
            if (name.StartsWith("#1/", StringComparison.Ordinal))
            {
                if (!int.TryParse(name[3..], NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count < 1 || count > 16_384 || count > size)
                    throw new InvalidDataException("A native archive member has an invalid extended name.");
                byte[] bytes = new byte[count];
                stream.ReadExactly(bytes);
                name = Encoding.UTF8.GetString(bytes).TrimEnd('\0');
                nameSize = count;
            }
            bool metadata = name is "/" or "//" or "/SYM64/" || name.TrimEnd('/').StartsWith("__.SYMDEF", StringComparison.Ordinal);
            if (!metadata)
            {
                if (platform == "emscripten" && architecture == "wasm32") WasmObjectVerifier.Verify(stream, end);
                else
                {
                    int length = (int)Math.Min(size - nameSize, buffer.Length);
                    stream.ReadExactly(buffer.AsSpan(0, length));
                    if (!IsObject(buffer.AsSpan(0, length), platform, architecture))
                        throw new InvalidDataException("A native archive member does not prove the requested object architecture: " + name);
                    if (byteOrder != NativeByteOrder.Unknown && !MatchesByteOrder(buffer.AsSpan(0, length), platform, byteOrder))
                        throw new InvalidDataException("A native archive member byte order differs from the resolved target: " + name);
                }
                members++;
            }
            stream.Position = end;
            if ((size & 1) != 0)
            {
                if (stream.ReadByte() != '\n') throw new InvalidDataException("A native archive member has invalid alignment padding.");
            }
        }
        if (members == 0) throw new InvalidDataException("A native archive contains no verified native object members.");
    }

    private static bool MatchesByteOrder(
        ReadOnlySpan<byte> bytes,
        string platform,
        NativeByteOrder expected
    ) {
        NativeByteOrder actual = platform switch
        {
            "windows" or "emscripten" => NativeByteOrder.LittleEndian,
            "linux" or "android" or "freebsd" when bytes.Length >= 6 => bytes[5] switch { 1 => NativeByteOrder.LittleEndian, 2 => NativeByteOrder.BigEndian, _ => NativeByteOrder.Unknown },
            "macos" or "ios" when bytes.Length >= 4 => BinaryPrimitives.ReadUInt32BigEndian(bytes) switch
            {
                0xfeedfacf or 0xfeedface => NativeByteOrder.BigEndian,
                0xcffaedfe or 0xcefaedfe => NativeByteOrder.LittleEndian,
                _ => NativeByteOrder.Unknown
            },
            _ => NativeByteOrder.Unknown
        };
        return actual == expected;
    }

    private static bool IsUniversalMachO(ReadOnlySpan<byte> bytes) => bytes.Length >= 8
        && BinaryPrimitives.ReadUInt32BigEndian(bytes) is 0xcafebabe or 0xcafebabf or 0xbebafeca or 0xbfbafeca;

    private static bool MachSharedSliceMatches(
        FileStream stream,
        ReadOnlySpan<byte> bytes,
        string architecture,
        NativeByteOrder expected
    ) {
        uint magic = BinaryPrimitives.ReadUInt32BigEndian(bytes);
        bool big = magic is 0xcafebabe or 0xcafebabf;
        bool wide = magic is 0xcafebabf or 0xbfbafeca;
        int entrySize = wide ? 32 : 20;
        uint count = big ? BinaryPrimitives.ReadUInt32BigEndian(bytes[4..]) : BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]);
        if (count > 100 || bytes.Length < 8 + (int)count * entrySize) return false;
        uint cpu = architecture switch { "x86" => 7, "x64" => 0x01000007, "arm" => 12, "arm64" => 0x0100000c, _ => 0 };
        Span<byte> header = stackalloc byte[32];
        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> entry = bytes.Slice(8 + index * entrySize, entrySize);
            uint candidate = big ? BinaryPrimitives.ReadUInt32BigEndian(entry) : BinaryPrimitives.ReadUInt32LittleEndian(entry);
            if (candidate != cpu) continue;
            ulong offset = wide ? big ? BinaryPrimitives.ReadUInt64BigEndian(entry[8..]) : BinaryPrimitives.ReadUInt64LittleEndian(entry[8..])
                : big ? BinaryPrimitives.ReadUInt32BigEndian(entry[8..]) : BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            ulong size = wide ? big ? BinaryPrimitives.ReadUInt64BigEndian(entry[16..]) : BinaryPrimitives.ReadUInt64LittleEndian(entry[16..])
                : big ? BinaryPrimitives.ReadUInt32BigEndian(entry[12..]) : BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
            if (offset < (ulong)(8 + count * entrySize) || size < 32 || offset > (ulong)stream.Length || size > (ulong)stream.Length - offset) return false;
            stream.Position = (long)offset;
            stream.ReadExactly(header);
            return !IsUniversalMachO(header) && IsMachO(header, architecture) && IsMachShared(header)
                && (expected == NativeByteOrder.Unknown || MatchesByteOrder(header, "macos", expected));
        }
        return false;
    }

    private static bool IsPe(
        ReadOnlySpan<byte> bytes,
        string architecture
    ) {
        if (bytes.Length < 0x40 || bytes[0] != 'M' || bytes[1] != 'Z')
            return false;
        int offset = BinaryPrimitives.ReadInt32LittleEndian(bytes[0x3c..]);
        if (offset < 0 || offset > bytes.Length - 24 || !bytes.Slice(offset, 4).SequenceEqual("PE\0\0"u8))
            return false;
        ushort machine = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(offset + 4)..]);
        ushort characteristics = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(offset + 22)..]);
        return (characteristics & 0x2000) != 0
            && machine == (architecture switch { "x86" => 0x014c, "x64" => 0x8664, "arm" => 0x01c4, "arm64" => 0xaa64, _ => 0 });
    }

    private static bool IsElf(
        ReadOnlySpan<byte> bytes,
        string architecture
    ) {
        if (bytes.Length < 20 || bytes[0] != 0x7f || bytes[1] != 'E' || bytes[2] != 'L' || bytes[3] != 'F' || bytes[4] != (architecture is "x86" or "arm" ? 1 : 2))
            return false;
        ushort machine = bytes[5] switch
        {
            1 => BinaryPrimitives.ReadUInt16LittleEndian(bytes[18..]),
            2 => BinaryPrimitives.ReadUInt16BigEndian(bytes[18..]),
            _ => (ushort)0
        };
        return machine == (architecture switch { "x86" => 3, "x64" => 62, "arm" => 40, "arm64" => 183, _ => 0 });
    }

    private static bool IsMachO(
        ReadOnlySpan<byte> bytes,
        string architecture
    ) {
        if (bytes.Length < 8)
            return false;
        uint magic = BinaryPrimitives.ReadUInt32BigEndian(bytes);
        uint expectedCpu = architecture switch { "x86" => 7, "x64" => 0x01000007u, "arm" => 12, "arm64" => 0x0100000cu, _ => 0 };
        if (magic is 0xfeedfacf or 0xfeedface)
            return BinaryPrimitives.ReadUInt32BigEndian(bytes[4..]) == expectedCpu;
        if (magic is 0xcffaedfe or 0xcefaedfe)
            return BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]) == expectedCpu;
        bool bigEndian = magic is 0xcafebabe or 0xcafebabf;
        bool littleEndian = magic is 0xbebafeca or 0xbfbafeca;
        if (!bigEndian && !littleEndian)
            return false;
        int entrySize = magic is 0xcafebabf or 0xbfbafeca ? 32 : 20;
        uint count = bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(bytes[4..]) : BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]);
        if (count > 100 || bytes.Length < 8 + (int)count * entrySize)
            return false;
        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> entry = bytes[(8 + index * entrySize)..];
            uint cpu = bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(entry) : BinaryPrimitives.ReadUInt32LittleEndian(entry);
            if (cpu == expectedCpu)
                return true;
        }

        return false;
    }
}
