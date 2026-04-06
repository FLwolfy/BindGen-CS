using System;
using System.Runtime.InteropServices;

namespace BGCS.CppAst.Parsing;
/// <summary>
/// Defines the public class <c>BumpAllocator</c>.
/// </summary>
public unsafe class BumpAllocator : IDisposable
{
    /// <summary>
    /// Exposes public member <c>4096</c>.
    /// </summary>
    public const int BlockSize = 4096;
    /// <summary>
    /// Exposes public member <c>8</c>.
    /// </summary>
    public const int Alignment = 8;
    private Block* head;
    private Block* tail;

    /// <summary>
    /// Executes public operation <c>new</c>.
    /// </summary>
    public static readonly BumpAllocator Shared = new();

    ~BumpAllocator()
    {
        DisposeCore();
    }

    /// <summary>
    /// Defines the public struct <c>Block</c>.
    /// </summary>
    public struct Block
    {
        /// <summary>
        /// Exposes public member <c>next</c>.
        /// </summary>
        public Block* next;
        private nuint used;

        /// <summary>
        /// Initializes a new instance of <see cref="Block"/>.
        /// </summary>
        public Block()
        {
        }

        private static byte* GetMemoryPtr(Block* self)
        {
            return (byte*)self + sizeof(Block);
        }

        /// <summary>
        /// Executes public operation <c>Alloc</c>.
        /// </summary>
        public byte* Alloc(Block* self, nuint size)
        {
            var offset = used;
            var newUsed = used + size;
            if (newUsed > BlockSize) return null;
            used = newUsed;
            return GetMemoryPtr(self) + offset;
        }

        /// <summary>
        /// Executes public operation <c>Reset</c>.
        /// </summary>
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

    /// <summary>
    /// Executes public operation <c>Alloc</c>.
    /// </summary>
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

    /// <summary>
    /// Executes public operation <c>Reset</c>.
    /// </summary>
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

    /// <summary>
    /// Executes public operation <c>Dispose</c>.
    /// </summary>
    public void Dispose()
    {
        DisposeCore();
        GC.SuppressFinalize(this);
    }
}
