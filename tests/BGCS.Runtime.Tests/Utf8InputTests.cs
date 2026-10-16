using System;
using System.Runtime.InteropServices;
using System.Text;
using BGCS.Runtime;
using Xunit;

namespace BGCS.Runtime.Tests;

public sealed class Utf8InputTests
{
    [Fact]
    public void Encode_PreservesNonBmpCodePointsAndAddsOneTerminator()
    {
        using NativeStringLease text = Utf8Codec.EncodeNullTerminated("A🐈", false);
        byte[] bytes = new byte[6];
        Marshal.Copy(text.pointer, bytes, 0, bytes.Length);
        Assert.Equal(new byte[] { 65, 0xf0, 0x9f, 0x90, 0x88, 0 }, bytes);
    }

    [Fact]
    public void Encode_RejectsEmbeddedNullAndInvalidUnicode()
    {
        Assert.Throws<ArgumentException>(() => Utf8Codec.EncodeNullTerminated("A\0B", false));
        Assert.Throws<EncoderFallbackException>(() => Utf8Codec.EncodeNullTerminated("\ud800", false));
    }

    [Fact]
    public void NullAllowance_IsExplicitAndDisposalIsIdempotent()
    {
        Assert.Throws<ArgumentNullException>(() => Utf8Codec.EncodeNullTerminated(null, false));
        using NativeStringLease absent = Utf8Codec.EncodeNullTerminated(null, true);
        Assert.Equal(0, absent.pointer);
        NativeStringLease text = Utf8Codec.EncodeNullTerminated("value", false);
        Assert.NotEqual(0, text.pointer);
        text.Dispose();
        text.Dispose();
        Assert.Equal(0, text.pointer);
    }
}
