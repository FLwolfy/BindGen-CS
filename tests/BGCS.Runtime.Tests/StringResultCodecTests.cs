using System;
using System.Runtime.InteropServices;
using System.Text;
using Xunit;

namespace BGCS.Runtime.Tests;

public sealed unsafe class StringResultCodecTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(4, false)]
    [InlineData(4, true)]
    public void ExactExtentsPreserveEmbeddedNullsAndNonBmpScalars(
        int width,
        bool bigEndian
    ) {
        const string value = "A\0😀B";
        byte[] bytes = Encoding(width, bigEndian).GetBytes(value);
        fixed (byte* pointer = bytes)
            Assert.Equal(value, DecodeLength((nint)pointer, bytes.Length / width, width, bigEndian));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(4, false)]
    [InlineData(4, true)]
    public void TerminatorAtGuardPageBoundaryStopsBeforeUnmappedMemory(
        int width,
        bool bigEndian
    ) {
        using GuardedPages pages = new();
        byte[] bytes = Encoding(width, bigEndian).GetBytes("A😀B\0");
        nint address = pages.readableEnd - bytes.Length;
        Marshal.Copy(bytes, 0, address, bytes.Length);
        Assert.Equal("A😀B", Decode(address, 1048576, width, bigEndian));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void InvalidUnicodeAndMissingTerminationFailWithinDeclaredStorage(int width)
    {
        byte[] invalid = width switch { 1 => [0xc0, 0xaf], 2 => [0x00, 0xd8], _ => [0x00, 0x00, 0x11, 0x00] };
        fixed (byte* pointer = invalid)
        {
            nint address = (nint)pointer;
            Assert.Throws<DecoderFallbackException>(() => DecodeLength(address, invalid.Length / width, width, false));
        }
        byte[] unterminated = Encoding(width, false).GetBytes("AB");
        fixed (byte* pointer = unterminated)
        {
            nint address = (nint)pointer;
            Assert.Throws<ArgumentException>(() => Decode(address, 2, width, false));
        }
    }

    [Fact]
    public void NullAndCheckedLengthArithmeticNeverReadInvalidAddresses()
    {
        Assert.Null(Utf8Codec.Decode(0));
        Assert.Null(Utf16Codec.Decode(0));
        Assert.Null(Utf32Codec.Decode(0));
        Assert.Equal(string.Empty, Utf8Codec.DecodeLength(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Utf8Codec.Decode(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Utf16Codec.DecodeLength(0, -1));
        Assert.Throws<OverflowException>(() => Utf32Codec.DecodeLength(1, int.MaxValue));
        Assert.Throws<OverflowException>(() => Utf16Codec.DecodeLength(unchecked((nint)(nuint.MaxValue - 1)), 2));
    }

    private static Encoding Encoding(
        int width,
        bool bigEndian
    ) => width switch
    { 1 => new UTF8Encoding(false, true), 2 => new UnicodeEncoding(bigEndian, false, true), _ => new UTF32Encoding(bigEndian, false, true) };
    private static string? Decode(
        nint address,
        int maximum,
        int width,
        bool bigEndian
    ) => width switch
    { 1 => Utf8Codec.Decode(address, maximum), 2 => Utf16Codec.Decode(address, maximum, bigEndian), _ => Utf32Codec.Decode(address, maximum, bigEndian) };
    private static string? DecodeLength(
        nint address,
        int count,
        int width,
        bool bigEndian
    ) => width switch
    { 1 => Utf8Codec.DecodeLength(address, count), 2 => Utf16Codec.DecodeLength(address, count, bigEndian), _ => Utf32Codec.DecodeLength(address, count, bigEndian) };

    private sealed class GuardedPages : IDisposable
    {
        private readonly nint m_start;
        private readonly nuint m_length = checked((nuint)Environment.SystemPageSize * 2);
        internal nint readableEnd => m_start + Environment.SystemPageSize;
        internal GuardedPages()
        {
            if (OperatingSystem.IsWindows())
            {
                m_start = VirtualAlloc(0, m_length, 0x3000, 0x04);
                if (m_start == 0) throw new InvalidOperationException("VirtualAlloc failed: " + Marshal.GetLastPInvokeError());
                if (!VirtualProtect(readableEnd, (nuint)Environment.SystemPageSize, 0x01, out _))
                { int error = Marshal.GetLastPInvokeError(); Dispose(); throw new InvalidOperationException("VirtualProtect failed: " + error); }
            }
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                m_start = mmap(0, m_length, 3, 2 | (OperatingSystem.IsMacOS() ? 0x1000 : 0x20), -1, 0);
                if (m_start == -1) throw new InvalidOperationException("mmap failed: " + Marshal.GetLastPInvokeError());
                if (mprotect(readableEnd, (nuint)Environment.SystemPageSize, 0) != 0)
                { int error = Marshal.GetLastPInvokeError(); Dispose(); throw new InvalidOperationException("mprotect failed: " + error); }
            }
            else throw new PlatformNotSupportedException("Guard-page decoding is verified on Windows, Linux and macOS.");
        }
        public void Dispose()
        {
            if (OperatingSystem.IsWindows()) _ = VirtualFree(m_start, 0, 0x8000);
            else _ = munmap(m_start, m_length);
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern nint VirtualAlloc(
            nint address,
            nuint size,
            uint allocationType,
            uint protection
        );
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool VirtualProtect(
            nint address,
            nuint size,
            uint protection,
            out uint previous
        );
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool VirtualFree(
            nint address,
            nuint size,
            uint type
        );
        [DllImport("libc", SetLastError = true)]
        private static extern nint mmap(
            nint address,
            nuint size,
            int protection,
            int flags,
            int file,
            nint offset
        );
        [DllImport("libc", SetLastError = true)]
        private static extern int mprotect(
            nint address,
            nuint size,
            int protection
        );
        [DllImport("libc", SetLastError = true)]
        private static extern int munmap(
            nint address,
            nuint size
        );
    }
}
