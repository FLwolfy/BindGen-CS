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
    /// Reinterprets an unmanaged value as an unsigned 64-bit representation.
    /// </summary>
    /// <typeparam name="T">Supported unmanaged integral type (1, 2, 4 or 8 bytes).</typeparam>
    /// <param name="value">Value to reinterpret.</param>
    /// <returns>Bitwise representation as <see cref="ulong"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ToULong<T>(T value) where T : unmanaged
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
    /// Reads a bitfield segment from <paramref name="raw"/>.
    /// </summary>
    /// <typeparam name="T">Underlying storage type.</typeparam>
    /// <param name="raw">Raw value containing the bitfield.</param>
    /// <param name="offset">Bit offset of the field.</param>
    /// <param name="bitWidth">Bit width of the field.</param>
    /// <returns>The extracted field value cast to <typeparamref name="T"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Get<T>(T raw, int offset, int bitWidth) where T : unmanaged
    {
        ulong rawl = ToULong(raw);
        ulong mask = (1UL << bitWidth) - 1UL;
        ulong value = (rawl >> offset) & mask;
        return *(T*)&value;
    }

    /// <summary>
    /// Writes a bitfield segment into <paramref name="raw"/>.
    /// </summary>
    /// <typeparam name="T">Underlying storage type.</typeparam>
    /// <param name="raw">Raw value to modify.</param>
    /// <param name="value">Field value to write.</param>
    /// <param name="offset">Bit offset of the field.</param>
    /// <param name="bitWidth">Bit width of the field.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Set<T>(ref T raw, T value, int offset, int bitWidth) where T : unmanaged
    {
        ulong rawl = ToULong(raw);
        ulong val = ToULong(value);
        ulong mask = ((1UL << bitWidth) - 1UL) << offset;
        var newl = (rawl & ~mask) | ((val & ((1UL << bitWidth) - 1UL)) << offset);
        raw = *(T*)&newl;
    }
}
