using System;
using System.IO;
using System.Text;

namespace BGCS.NativeBuild.Validation;

/// <summary>Checks bounded LLVM-style relocatable Wasm objects and their actual memory-index width.</summary>
internal static class WasmObjectVerifier
{
    internal static void Verify(
        Stream stream,
        long end
    ) {
        Span<byte> magic = stackalloc byte[8];
        if (end - stream.Position < magic.Length) throw Invalid("The Wasm object header is truncated.");
        stream.ReadExactly(magic);
        if (!magic.SequenceEqual("\0asm\x01\0\0\0"u8)) throw Invalid("The native static member is not a supported Wasm module binary.");
        bool linking = false, memory32 = false;
        int sections = 0;
        while (stream.Position < end)
        {
            if (++sections > 100_000) throw Invalid("The Wasm object exceeds its section-count limit.");
            int kind = ReadByte(stream, end);
            uint length = ReadU32(stream, end);
            if (length > end - stream.Position) throw Invalid("A Wasm section extends outside its object member.");
            long sectionEnd = stream.Position + length;
            switch (kind)
            {
                case 0:
                    string name = ReadName(stream, sectionEnd);
                    if (name == "linking")
                    {
                        if (linking || ReadU32(stream, sectionEnd) != 2) throw Invalid("The Wasm object linking metadata is duplicated or has an unsupported revision.");
                        linking = true;
                    }
                    else if (name == "target_features")
                    {
                        uint count = ReadCount(stream, sectionEnd);
                        for (uint feature = 0; feature < count; feature++)
                        {
                            int prefix = ReadByte(stream, sectionEnd);
                            string value = ReadName(stream, sectionEnd);
                            if (prefix is not ('+' or '-' or '?')) throw Invalid("The Wasm object has an unknown target-feature convention.");
                            if (prefix == '+' && value is "memory64" or "table64") throw Invalid("A wasm64 native object cannot be staged as wasm32.");
                        }
                    }
                    break;
                case 2:
                    uint imports = ReadCount(stream, sectionEnd);
                    for (uint import = 0; import < imports; import++)
                    {
                        ReadName(stream, sectionEnd); ReadName(stream, sectionEnd);
                        switch (ReadByte(stream, sectionEnd))
                        {
                            case 0: ReadU32(stream, sectionEnd); break;
                            case 1: ReadValueType(stream, sectionEnd); ReadLimits(stream, sectionEnd); break;
                            case 2: ReadLimits(stream, sectionEnd); memory32 = true; break;
                            case 3:
                                ReadValueType(stream, sectionEnd);
                                if (ReadByte(stream, sectionEnd) > 1) throw Invalid("A Wasm global import has invalid mutability.");
                                break;
                            case 4:
                                if (ReadByte(stream, sectionEnd) != 0) throw Invalid("A Wasm tag import has an unknown attribute.");
                                ReadU32(stream, sectionEnd);
                                break;
                            default: throw Invalid("The Wasm object has an unsupported import descriptor.");
                        }
                    }
                    if (stream.Position != sectionEnd) throw Invalid("The Wasm import section has trailing or malformed bytes.");
                    break;
                case 5:
                    uint memories = ReadCount(stream, sectionEnd);
                    for (uint memory = 0; memory < memories; memory++) { ReadLimits(stream, sectionEnd); memory32 = true; }
                    if (stream.Position != sectionEnd) throw Invalid("The Wasm memory section has trailing or malformed bytes.");
                    break;
                case > 13: throw Invalid("The Wasm object uses an unknown core section convention.");
            }
            stream.Position = sectionEnd;
        }
        if (!linking) throw Invalid("Native Wasm assets require relocatable object linking metadata; an executable module is insufficient.");
        if (!memory32) throw Invalid("The Wasm object does not independently declare 32-bit linear-memory addressing.");
    }
    private static void ReadLimits(
        Stream stream,
        long end
    ) {
        uint flags = ReadU32(stream, end);
        if ((flags & 4) != 0) throw Invalid("A memory64/table64 native object cannot be staged as wasm32.");
        if ((flags & ~3u) != 0) throw Invalid("The Wasm object has unsupported memory or table limits.");
        uint minimum = ReadU32(stream, end);
        if ((flags & 1) != 0 && ReadU32(stream, end) < minimum) throw Invalid("The Wasm object's maximum limit is smaller than its minimum.");
        if ((flags & 2) != 0 && (flags & 1) == 0) throw Invalid("A shared Wasm memory requires an explicit maximum.");
    }
    private static void ReadValueType(
        Stream stream,
        long end
    ) {
        int kind = ReadByte(stream, end);
        if (kind is 0x63 or 0x64)
        {
            for (int index = 0; index < 5; index++) if ((ReadByte(stream, end) & 0x80) == 0) return;
            throw Invalid("A Wasm reference heap type exceeds its encoding bound.");
        }
        if (kind is not (0x7f or 0x7e or 0x7d or 0x7c or 0x7b or 0x70 or 0x6f or 0x6e or 0x6d or 0x6c or 0x6b or 0x6a or 0x69 or 0x68))
            throw Invalid("The Wasm object has an unknown imported value type.");
    }
    private static uint ReadCount(
        Stream stream,
        long end
    ) {
        uint count = ReadU32(stream, end);
        return count <= 100_000 ? count : throw Invalid("A Wasm object metadata vector exceeds its bounded count.");
    }
    private static string ReadName(
        Stream stream,
        long end
    ) {
        uint length = ReadU32(stream, end);
        if (length > 16_384 || length > end - stream.Position) throw Invalid("A Wasm metadata name is too large or truncated.");
        byte[] bytes = new byte[(int)length];
        stream.ReadExactly(bytes);
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException failure) { throw new InvalidDataException("A Wasm metadata name has invalid UTF-8.", failure); }
    }
    private static uint ReadU32(
        Stream stream,
        long end
    ) {
        uint result = 0;
        for (int index = 0; index < 5; index++)
        {
            uint octet = (uint)ReadByte(stream, end);
            if (index == 4 && (octet & 0xf0) != 0) throw Invalid("A Wasm varuint32 exceeds its encoding bound.");
            result |= (octet & 0x7f) << (index * 7);
            if ((octet & 0x80) == 0) return result;
        }
        throw Invalid("A Wasm varuint32 is unterminated.");
    }
    private static int ReadByte(
        Stream stream,
        long end
    ) => stream.Position < end && stream.ReadByte() is int value && value >= 0 ? value : throw Invalid("The Wasm object metadata is truncated.");
    private static InvalidDataException Invalid(string message) => new(message);
}
