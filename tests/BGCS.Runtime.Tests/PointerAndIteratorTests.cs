using System;
using BGCS.Runtime;
using Xunit;

namespace BGCS.Runtime.Tests;

public unsafe class PointerAndIteratorTests
{
    [Fact]
    public void Pointer_WithAddressArithmetic_ShouldAdvanceByElementSize()
    {
        Pointer<int> ptr = (Pointer<int>)(nint)100;

        Pointer<int> next = ptr + 2;
        Pointer<int> prev = next - 1;
        Pointer<int> inc = ++ptr;
        Pointer<int> dec = --inc;

        Assert.Equal((nint)108, (nint)next);
        Assert.Equal((nint)104, (nint)prev);
        Assert.Equal((nint)100, (nint)dec);
    }

    [Fact]
    public void ConstPointer_WithAddressArithmetic_ShouldAdvanceByElementSize()
    {
        ConstPointer<int> ptr = (ConstPointer<int>)(nint)100;

        ConstPointer<int> next = ptr + 3;
        ConstPointer<int> prev = next - 2;

        Assert.Equal((nint)112, (nint)next);
        Assert.Equal((nint)104, (nint)prev);
        Assert.True(ptr == (nint)100);
        Assert.True(ptr != (nint)104);
    }

    [Fact]
    public void Pointer_Indexer_ShouldReadAndWriteTargetMemory()
    {
        nint memory = System.Runtime.InteropServices.Marshal.AllocHGlobal(sizeof(int) * 3);
        try
        {
            int* p = (int*)memory;
            p[0] = 1;
            p[1] = 2;
            p[2] = 3;

            Pointer<int> pointer = new(p);
            pointer[1] = 20;

            Assert.Equal(1, pointer[0]);
            Assert.Equal(20, pointer[1]);
            Assert.Equal(3, pointer[2]);
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(memory);
        }
    }

    [Fact]
    public void Iterator_MoveNext_ShouldUpdateCurrentPointer()
    {
        nint memory = System.Runtime.InteropServices.Marshal.AllocHGlobal(sizeof(int) * 2);
        try
        {
            int* p = (int*)memory;
            p[0] = 7;
            p[1] = 9;

            Iterator<int> iterator = new(p);
            Assert.Equal((nint)p, (nint)iterator.Current);
            Assert.Equal(7, *iterator.Current);

            iterator.MoveNext();

            Assert.Equal((nint)(p + 1), (nint)iterator.Current);
            Assert.Equal(9, *iterator.Current);
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(memory);
        }
    }
}
