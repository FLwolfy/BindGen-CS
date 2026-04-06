using System;
using BGCS.Runtime;
using Xunit;

namespace BGCS.Runtime.Tests;

public unsafe class MemoryPoolTests
{
    [Fact]
    public void Alloc_ShouldReturnWritableMemory_AndFreeShouldNotThrow()
    {
        int* ptr = (int*)MemoryPool.Alloc<int>(4);
        Assert.NotEqual((nint)0, (nint)ptr);

        ptr[0] = 11;
        ptr[1] = 22;
        ptr[2] = 33;
        ptr[3] = 44;

        Assert.Equal(11, ptr[0]);
        Assert.Equal(22, ptr[1]);
        Assert.Equal(33, ptr[2]);
        Assert.Equal(44, ptr[3]);

        Exception? ex = Record.Exception(() => MemoryPool.Free(ptr));
        Assert.Null(ex);
    }

    [Fact]
    public void Alloc_TwoCalls_ShouldReturnDistinctPointersInCurrentImplementation()
    {
        int* a = (int*)MemoryPool.Alloc<int>(1);
        int* b = (int*)MemoryPool.Alloc<int>(1);

        try
        {
            Assert.NotEqual((nint)a, (nint)b);
        }
        finally
        {
            MemoryPool.Free(a);
            MemoryPool.Free(b);
        }
    }
}
