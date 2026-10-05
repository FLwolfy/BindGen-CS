using System;
using System.Text;
using Xunit;

namespace BGCS.Runtime.Tests;

public unsafe class UtilsAndAttributesTests
{
    private delegate int IntUnaryDelegate(int value);

    [Fact]
    public void Utf8PointerRoundTrip_ShouldEncodeAndDecode()
    {
        const string text = "Hello 世界";
        byte* ptr = Utils.StringToUTF8Ptr(text);
        try
        {
            string decoded = Utils.DecodeStringUTF8(ptr);
            int cstrLen = Utils.CStringLength(ptr);

            Assert.Equal(text, decoded);
            Assert.Equal(Utils.GetByteCountUTF8(text), cstrLen);
        }
        finally
        {
            Utils.Free(ptr);
        }
    }

    [Fact]
    public void Utf16PointerRoundTrip_ShouldEncodeAndDecode()
    {
        const string text = "Hi Runtime";
        char* ptr = Utils.StringToUTF16Ptr(text);
        try
        {
            string decoded = Utils.DecodeStringUTF16(ptr);
            int cstrLen = Utils.CStringLength(ptr);

            Assert.Equal(text, decoded);
            Assert.Equal(text.Length, cstrLen);
        }
        finally
        {
            Utils.Free(ptr);
        }
    }

    [Fact]
    public void DelegatePointerRoundTrip_ShouldReturnEquivalentDelegate()
    {
        static int AddOne(int value) => value + 1;

        IntUnaryDelegate func = AddOne;
        nint ptr = Utils.GetFunctionPointerForDelegate(func);
        IntUnaryDelegate? restored = Utils.GetDelegateForFunctionPointer<IntUnaryDelegate>((void*)ptr);

        Assert.NotEqual((nint)0, ptr);
        Assert.NotNull(restored);
        Assert.Equal(11, restored!(10));
    }

    [Fact]
    public void DelegatePointerHelpers_WithNullValues_ShouldReturnNullPointerAndNullDelegate()
    {
        nint ptr = Utils.GetFunctionPointerForDelegate<IntUnaryDelegate>(null);
        IntUnaryDelegate? restored = Utils.GetDelegateForFunctionPointer<IntUnaryDelegate>((void*)0);

        Assert.Equal((nint)0, ptr);
        Assert.Null(restored);
    }

    [Fact]
    public void EncodeStringUtf8AndUtf16_ShouldWriteExpectedBytes()
    {
        const string text = "abcXYZ";

        int utf8Size = Utils.GetByteCountUTF8(text);
        byte* utf8Buffer = stackalloc byte[utf8Size];
        int utf8Written = Utils.EncodeStringUTF8(text, utf8Buffer, utf8Size);
        string utf8Decoded = Encoding.UTF8.GetString(utf8Buffer, utf8Written);

        int utf16Size = Utils.GetByteCountUTF16(text);
        char* utf16Buffer = stackalloc char[text.Length];
        int utf16Written = Utils.EncodeStringUTF16(text, utf16Buffer, utf16Size);
        string utf16Decoded = new(utf16Buffer, 0, utf16Written / sizeof(char));

        Assert.Equal(utf8Size, utf8Written);
        Assert.Equal(text, utf8Decoded);
        Assert.Equal(utf16Size, utf16Written);
        Assert.Equal(text, utf16Decoded);
    }

    [Fact]
    public void CStringLength_WithNullPointer_ShouldThrowArgumentNullException()
    {
        byte* nullByte = null;
        char* nullChar = null;

        Assert.Throws<ArgumentNullException>(() => Utils.CStringLength(nullByte));
        Assert.Throws<ArgumentNullException>(() => Utils.CStringLength(nullChar));
    }

    [Fact]
    public void BstrRoundTrip_ShouldEncodeDecodeAndFree()
    {
        const string text = "BSTR test";
        void* bstr = Utils.StringToBSTR(text);
        try
        {
            string decoded = Utils.DecodeStringBSTR(bstr);
            Assert.Equal(text, decoded);
        }
        finally
        {
            Utils.FreeBSTR(bstr);
        }
    }

    [Fact]
    public void GetByteCountArray_UsesTheActualUnmanagedElementSize()
    {
        int[] values = [1, 2, 3];

        int bytes = Utils.GetByteCountArray(values);

        Assert.Equal(values.Length * sizeof(int), bytes);
        Assert.Equal(3, Utils.GetByteCountArray(new byte[3]));
        Assert.Equal(24, Utils.GetByteCountArray(new long[3]));
        Assert.Throws<ArgumentNullException>(() => Utils.GetByteCountArray<int>(null!));
    }

    [Fact]
    public void Alloc_RejectsInvalidSizesBeforeAllocatingAndSupportsEmptyStorage()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Utils.Alloc<int>(-1));
        Assert.Throws<OverflowException>(() => Utils.Alloc<long>(int.MaxValue));
        Assert.Equal((nint)0, (nint)Utils.Alloc<byte>(0));
        Utils.Free(null);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Unicode 😀")]
    public void Utf16Allocation_IncludesTheTerminatorInOwnedStorage(string text)
    {
        char* pointer = Utils.StringToUTF16Ptr(text);
        try
        {
            Assert.Equal(text, Utils.DecodeStringUTF16(pointer));
            Assert.Equal('\0', pointer[text.Length]);
        }
        finally
        {
            Utils.Free(pointer);
        }
    }

    [Fact]
    public void NativeAttributes_Constructors_ShouldPopulateProperties()
    {
        NativeNameAttribute byName = new("NativeFoo");
        NativeNameAttribute byType = new(NativeNameType.Func, "NativeBar");
        SourceLocationAttribute source = new("header.h", "1:1", "1:10");

        Assert.Equal("NativeFoo", byName.name);
        Assert.Equal(default, byName.type);
        Assert.Equal(NativeNameType.Func, byType.type);
        Assert.Equal("NativeBar", byType.name);
        Assert.Equal("header.h", source.file);
        Assert.Equal("1:1", source.start);
        Assert.Equal("1:10", source.end);
    }
}
