using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace BGCS.Runtime.Tests;

public sealed class CustomAllocatorCodecTests
{
    [Theory]
    [InlineData(8, false, "41F09F988000")]
    [InlineData(16, false, "41003DD800DE0000")]
    [InlineData(16, true, "0041D83DDE000000")]
    [InlineData(32, false, "4100000000F6010000000000")]
    [InlineData(32, true, "000000410001F60000000000")]
    public void TextEncoding_PreservesExactBytesAndMatchingAlignedPair(
        int width,
        bool bigEndian,
        string expected
    ) {
        Counters counters = new();
        NativeAllocator allocator = CreateAllocator(counters);
        NativeStringLease text = Encode(width, bigEndian, "A😀", false, allocator);
        Assert.Equal((nuint)0, (nuint)text.pointer % 64);
        byte[] actual = new byte[expected.Length / 2];
        Marshal.Copy(text.pointer, actual, 0, actual.Length);
        Assert.Equal(Convert.FromHexString(expected), actual);
        Assert.Equal(1, counters.allocations);
        Assert.Equal((nuint)64, counters.physicalLength);
        Assert.Equal((nuint)64, counters.alignment);
        text.Dispose(); text.Dispose();
        Assert.Equal((nint)0, text.pointer);
        Assert.Equal(1, counters.frees);
        Assert.Null(text.releaseFailure);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void InvalidOrNullText_IsCheckedBeforeCallingCustomAllocator(int width)
    {
        Counters counters = new();
        NativeAllocator allocator = CreateAllocator(counters);
        using NativeStringLease absent = Encode(width, false, null, true, allocator);
        Assert.Equal((nint)0, absent.pointer);
        Assert.Throws<ArgumentNullException>(() => Encode(width, false, null, false, allocator));
        Assert.Throws<ArgumentException>(() => Encode(width, false, "A\0B", false, allocator));
        Assert.Throws<EncoderFallbackException>(() => Encode(width, false, "\ud800", false, allocator));
        Assert.Equal(0, counters.allocations);
        Assert.Equal(0, counters.frees);
    }

    [Fact]
    public async Task ClosingModule_KeepsMatchingTextCleanupAliveUntilActualFree()
    {
        Counters counters = new();
        NativeModuleSession session = new(new StaticModuleProvider(new Dictionary<string, nint>(), () => counters.unloads++));
        NativeAllocator allocator = CreateAllocator(counters, session);
        NativeStringLease text = Encode(8, false, "value", false, allocator);
        Task closing = session.DisposeAsync().AsTask();
        Assert.False(closing.IsCompleted);
        Assert.Throws<ObjectDisposedException>(() => Encode(8, false, "new", false, allocator));
        await text.DisposeAsync();
        await closing;
        Assert.Equal(1, counters.frees);
        Assert.Equal(1, counters.unloads);
    }

    [Fact]
    public async Task AmbiguousTextFree_ThrowsOnExplicitDisposeWithoutRetryOrModuleUnload()
    {
        Counters counters = new();
        using NativeModuleSession session = new(new StaticModuleProvider(new Dictionary<string, nint>(), () => counters.unloads++));
        NativeStringLease text = Encode(32, false, "value", false, CreateAllocator(counters, session, throwAfterFree: true));
        session.Dispose();
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(text.Dispose);
        Assert.Same(text.releaseFailure, failure.InnerException);
        Assert.Throws<InvalidOperationException>(text.Dispose);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await text.DisposeAsync());
        Assert.Equal(1, counters.frees);
        Assert.Equal(0, counters.unloads);
    }

    [Fact]
    public void AffineTextDisposeAsync_WaitsForOwnerThreadFree()
    {
        Counters counters = new();
        Pump dispatcher = new();
        NativeStringLease text = Encode(16, false, "value", false, CreateAllocator(counters, dispatcher: dispatcher));
        Task? cleanup = null;
        Thread worker = new(() => cleanup = text.DisposeAsync().AsTask());
        worker.Start();
        Assert.True(worker.Join(TimeSpan.FromSeconds(5)));
        Task actualCleanup = cleanup ?? throw new InvalidOperationException("Text disposal did not start.");
        Assert.False(actualCleanup.IsCompleted);
        Assert.Equal((nint)0, text.pointer);
        Assert.Equal(0, counters.frees);
        dispatcher.Drain();
        Assert.True(actualCleanup.IsCompletedSuccessfully);
        Assert.Equal(1, counters.frees);
    }

    [Fact]
    public void AbandonedText_FinalizesOriginalBufferBeforeModuleUnload()
    {
        Counters counters = new();
        using NativeModuleSession session = new(new StaticModuleProvider(new Dictionary<string, nint>(), () => Interlocked.Increment(ref counters.unloads)));
        NativeAllocator allocator = CreateAllocator(counters, session);
        WeakReference abandoned = Abandon(allocator);
        session.Dispose();
        for (int attempt = 0; attempt < 3 && abandoned.IsAlive; ++attempt)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        }
        Assert.False(abandoned.IsAlive);
        Assert.Equal(1, counters.frees);
        Assert.Equal(1, counters.unloads);
        GC.KeepAlive(allocator);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Abandon(NativeAllocator allocator) => new(Encode(8, false, "abandoned", false, allocator));

    private static NativeStringLease Encode(
        int width,
        bool bigEndian,
        string? value,
        bool allowsNull,
        NativeAllocator allocator
    )
        => width switch
        {
            8 => Utf8Codec.EncodeNullTerminated(value, allowsNull, 64, allocator),
            16 => Utf16Codec.EncodeNullTerminated(value, allowsNull, bigEndian, 64, allocator),
            32 => Utf32Codec.EncodeNullTerminated(value, allowsNull, bigEndian, 64, allocator),
            _ => throw new ArgumentOutOfRangeException(nameof(width))
        };

    private static unsafe NativeAllocator CreateAllocator(
        Counters counters,
        NativeModuleSession? session = null,
        bool throwAfterFree = false,
        Pump? dispatcher = null
    )
        => new("text-allocation-pair", (
            length,
            alignment
        ) => {
            if (dispatcher is not null) Assert.True(dispatcher.CheckAccess());
            counters.allocations++;
            counters.physicalLength = length;
            counters.alignment = alignment;
            return (nint)NativeMemory.AlignedAlloc(length, alignment);
        }, (
            address,
            length,
            alignment
        ) => {
            if (dispatcher is not null) Assert.True(dispatcher.CheckAccess());
            Assert.Equal(0, Volatile.Read(ref counters.unloads));
            Assert.Equal(counters.physicalLength, length);
            Assert.Equal(counters.alignment, alignment);
            NativeMemory.AlignedFree((void*)address);
            Interlocked.Increment(ref counters.frees);
            if (throwAfterFree) throw new InvalidOperationException("The native cleanup result is ambiguous.");
        }, dispatcher is null ? NativeAllocatorThreadDomain.AnyThread : NativeAllocatorThreadDomain.Dispatcher, session, dispatcher);

    private sealed class Counters
    {
        internal int allocations;
        internal int frees;
        internal int unloads;
        internal nuint physicalLength;
        internal nuint alignment;
    }

    private sealed class Pump : INativeReleaseDispatcher
    {
        private readonly int m_thread = Environment.CurrentManagedThreadId;
        private readonly ConcurrentQueue<Action> m_pending = new();
        public bool CheckAccess() => Environment.CurrentManagedThreadId == m_thread;
        public void Post(Action cleanup) => m_pending.Enqueue(cleanup);
        internal void Drain()
        {
            Assert.True(CheckAccess());
            while (m_pending.TryDequeue(out Action? cleanup)) cleanup();
        }
    }
}
