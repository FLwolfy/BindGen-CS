namespace BGCS.Runtime;

using System;
using System.Runtime.CompilerServices;

/// <summary>
/// A utility for accessing bit fields.
///
/// Fast enough™
///
/// .NET 9.0.7 (9.0.725.31616), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
/// HardwareIntrinsics=AVX-512F+CD+BW+DQ+VL+VBMI,AES,BMI1,BMI2,FMA,LZCNT,PCLMUL,POPCNT VectorSize=256
///
/// | Method | Mean     | Error     | StdDev    |
/// |------- |---------:|----------:|----------:|
/// | GetA   | 1.077 ns | 0.0078 ns | 0.0065 ns |
/// | SetA   | 1.930 ns | 0.0127 ns | 0.0106 ns |
/// </summary>
public static unsafe class Bitfield
{
    /// <summary>
    /// Extracts a little-endian native bit range without assuming an integer allocation unit.
    /// </summary>
    /// <param name="raw">Borrowed bytes containing the entire requested range.</param>
    /// <param name="offset">Offset of the first bit from the start of the bytes.</param>
    /// <param name="bitWidth">Number of bits to extract, between one and 64.</param>
    /// <returns>The zero-extended field value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The range is invalid or exceeds the supplied bytes.</exception>
    public static ulong Get(
        ReadOnlySpan<byte> raw,
        int offset,
        int bitWidth
    ) {
        ValidateByteRange(raw.Length, offset, bitWidth);
        ulong value = 0;
        int written = 0;
        while (written < bitWidth)
        {
            long position = (long)offset + written;
            int shift = (int)(position % 8);
            int count = Math.Min(8 - shift, bitWidth - written);
            ulong bits = (ulong)(raw[(int)(position / 8)] >> shift) & CreateMask(count);
            value |= bits << written;
            written += count;
        }

        return value;
    }

    /// <summary>
    /// Extracts and sign-extends a little-endian native bit range.
    /// </summary>
    /// <param name="raw">Borrowed bytes containing the entire requested range.</param>
    /// <param name="offset">Offset of the first bit from the start of the bytes.</param>
    /// <param name="bitWidth">Number of bits to extract, between one and 64.</param>
    /// <returns>The field value sign-extended to 64 bits.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The range is invalid or exceeds the supplied bytes.</exception>
    public static long GetSigned(
        ReadOnlySpan<byte> raw,
        int offset,
        int bitWidth
    ) {
        ulong value = Get(raw, offset, bitWidth);
        if ((value & (1UL << (bitWidth - 1))) != 0)
            value |= ~CreateMask(bitWidth);
        return unchecked((long)value);
    }

    /// <summary>
    /// Updates a little-endian native bit range while preserving every neighboring bit.
    /// </summary>
    /// <param name="raw">Borrowed mutable bytes containing the entire requested range.</param>
    /// <param name="value">Field value; high bits outside the field are discarded.</param>
    /// <param name="offset">Offset of the first bit from the start of the bytes.</param>
    /// <param name="bitWidth">Number of bits to update, between one and 64.</param>
    /// <exception cref="ArgumentOutOfRangeException">The range is invalid or exceeds the supplied bytes.</exception>
    public static void Set(
        Span<byte> raw,
        ulong value,
        int offset,
        int bitWidth
    ) {
        ValidateByteRange(raw.Length, offset, bitWidth);
        int written = 0;
        while (written < bitWidth)
        {
            long position = (long)offset + written;
            int shift = (int)(position % 8);
            int count = Math.Min(8 - shift, bitWidth - written);
            int mask = (int)CreateMask(count) << shift;
            int bits = (int)((value >> written) & CreateMask(count)) << shift;
            int byteIndex = (int)(position / 8);
            raw[byteIndex] = (byte)((raw[byteIndex] & ~mask) | bits);
            written += count;
        }
    }

    /// <summary>
    /// Reinterprets an unmanaged value as an unsigned 64-bit representation.
    /// </summary>
    /// <typeparam name = "T">Supported unmanaged integral type (1, 2, 4 or 8 bytes).</typeparam>
    /// <param name = "value">Value to reinterpret.</param>
    /// <returns>Bitwise representation as <see cref = "ulong "/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ToULong<T>(T value)
        where T : unmanaged
    {
        return sizeof(T) switch
        {
            1 => Unsafe.BitCast<T, byte>(value),
            2 => Unsafe.BitCast<T, ushort>(value),
            4 => Unsafe.BitCast<T, uint>(value),
            8 => Unsafe.BitCast<T, ulong>(value),
            _ => throw new Exception($"Type '{typeof(T)} is not supported in bitfields.'"),
        };
    }

    /// <summary>
    /// Reads a bitfield segment from <paramref name = "raw"/>.
    /// </summary>
    /// <typeparam name = "T">Underlying storage type.</typeparam>
    /// <param name = "raw">Raw value containing the bitfield.</param>
    /// <param name = "offset">Bit offset of the field.</param>
    /// <param name = "bitWidth">Bit width of the field.</param>
    /// <returns>The extracted field value cast to <typeparamref name = "T"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Get<T>(
        T raw,
        int offset,
        int bitWidth
    )
        where T : unmanaged
    {
        ValidateRange<T>(offset, bitWidth);
        ulong value = (ToULong(raw) >> offset) & CreateMask(bitWidth);
        return *(T*)&value;
    }

    /// <summary>
    /// Reads and sign-extends a signed bitfield segment from <paramref name = "raw"/>.
    /// </summary>
    /// <typeparam name = "T">Underlying signed storage type.</typeparam>
    /// <param name = "raw">Raw value containing the bitfield.</param>
    /// <param name = "offset">Bit offset of the field.</param>
    /// <param name = "bitWidth">Bit width of the field.</param>
    /// <returns>The sign-extended field value cast to <typeparamref name = "T"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T GetSigned<T>(
        T raw,
        int offset,
        int bitWidth
    )
        where T : unmanaged
    {
        ValidateRange<T>(offset, bitWidth);
        ulong mask = CreateMask(bitWidth);
        ulong value = (ToULong(raw) >> offset) & mask;
        ulong signBit = 1UL << (bitWidth - 1);
        if ((value & signBit) != 0)
        {
            value |= ~mask;
        }

        return *(T*)&value;
    }

    /// <summary>
    /// Writes a bitfield segment into <paramref name = "raw"/>.
    /// </summary>
    /// <typeparam name = "T">Underlying storage type.</typeparam>
    /// <param name = "raw">Raw value to modify.</param>
    /// <param name = "value">Field value to write.</param>
    /// <param name = "offset">Bit offset of the field.</param>
    /// <param name = "bitWidth">Bit width of the field.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Set<T>(
        ref T raw,
        T value,
        int offset,
        int bitWidth
    )
        where T : unmanaged
    {
        ValidateRange<T>(offset, bitWidth);
        ulong rawValue = ToULong(raw);
        ulong valueMask = CreateMask(bitWidth);
        ulong mask = valueMask << offset;
        ulong newValue = (rawValue & ~mask) | ((ToULong(value) & valueMask) << offset);
        raw = *(T*)&newValue;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong CreateMask(int bitWidth) => bitWidth == 64 ? ulong.MaxValue : (1UL << bitWidth) - 1UL;

    private static void ValidateByteRange(
        int byteCount,
        int offset,
        int bitWidth
    ) {
        if (offset < 0 || bitWidth is < 1 or > 64 || (long)offset + bitWidth > (long)byteCount * 8)
            throw new ArgumentOutOfRangeException(nameof(bitWidth), "The bit range must fit the supplied bytes and contain one to 64 bits.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ValidateRange<T>(
        int offset,
        int bitWidth
    )
        where T : unmanaged
    {
        int storageBits = sizeof(T) * 8;
        if (bitWidth <= 0 || offset < 0 || bitWidth > storageBits || offset > storageBits - bitWidth)
        {
            throw new ArgumentOutOfRangeException(nameof(bitWidth), $"Bit range [{offset}, {offset + bitWidth}) exceeds {storageBits}-bit storage.");
        }
    }
}
