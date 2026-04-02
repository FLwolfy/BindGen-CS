using System;
using System.Runtime.InteropServices;

namespace BGCS.CppAst.Parsing;
public unsafe class BumpAllocator : IDisposable
{
    public const int BlockSize = 4096;
    public const int Alignment = 8;
    private Block* head;
    private Block* tail;

    public static readonly BumpAllocator Shared = new();

    ~BumpAllocator()
    {
        DisposeCore();
    }

    public struct Block
    {
        public Block* next;
        private nuint used;

        public Block()
        {
        }

        private static byte* GetMemoryPtr(Block* self)
        {
            return (byte*)self + sizeof(Block);
        }

        public byte* Alloc(Block* self, nuint size)
        {
            var offset = used;
            var newUsed = used + size;
            if (newUsed > BlockSize) return null;
            used = newUsed;
            return GetMemoryPtr(self) + offset;
        }

        public void Reset()
        {
            used = 0;
        }
    }

    private byte* AllocNewBlock(nuint newAllocSize = 0)
    {
        nuint memoryToAlloc = (nuint)(sizeof(Block) + BlockSize);
        Block* block = (Block*)NativeMemory.AlignedAlloc(memoryToAlloc, Alignment);
        *block = default;

        if (head == null) head = block;
        if (tail != null) tail->next = block;
        tail = block;

        return block->Alloc(block, newAllocSize);
    }

    public byte* Alloc(nuint size)
    {
        if (tail == null)
        {
            return AllocNewBlock(size);
        }
        var ptr = tail->Alloc(tail, size);
        if (ptr == null)
        {
            return AllocNewBlock(size);
        }
        return ptr;
    }

    public void Reset()
    {
        Block* current = head;
        while (current != null)
        {
            current->Reset();
            current = current->next;
        }
    }

    protected virtual void DisposeCore()
    {
        Block* current = head;
        while (current != null)
        {
            var next = current->next; // Fetch before free.
            NativeMemory.AlignedFree(current);
            current = next;
        }
    }

    public void Dispose()
    {
        DisposeCore();
        GC.SuppressFinalize(this);
    }
}
