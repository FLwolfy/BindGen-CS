using System;
using System.Collections.Generic;
using System.Text.Json;
using BGCS.Runtime;

internal static unsafe class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Supply the built native custom allocator library path.");
        // The session provider owns this module through final cleanup, including ambiguous free failure.
        DynamicModuleProvider native = new(args[0]);
        string[] names = ["example_allocator_create", "example_allocator_destroy", "example_allocate", "example_release",
            "example_allocations", "example_releases", "example_checksum"];
        Dictionary<string, nint> exports = new(StringComparer.Ordinal);
        foreach (string name in names)
        {
            if (!native.TryGetExport(name, out nint address))
            { native.Dispose(); throw new EntryPointNotFoundException("Missing native export " + name); }
            exports.Add(name, address);
        }
        var create = (delegate* unmanaged[Cdecl]<nint>)exports["example_allocator_create"];
        var destroy = (delegate* unmanaged[Cdecl]<nint, int>)exports["example_allocator_destroy"];
        var allocate = (delegate* unmanaged[Cdecl]<nint, nuint, nuint, nint>)exports["example_allocate"];
        var release = (delegate* unmanaged[Cdecl]<nint, nint, nuint, nuint, int>)exports["example_release"];
        var allocationCount = (delegate* unmanaged[Cdecl]<nint, ulong>)exports["example_allocations"];
        var releaseCount = (delegate* unmanaged[Cdecl]<nint, ulong>)exports["example_releases"];
        var checksum = (delegate* unmanaged[Cdecl]<byte*, nuint, ulong>)exports["example_checksum"];
        nint state = create();
        if (state == 0) { native.Dispose(); throw new OutOfMemoryException("The native allocator state could not be created."); }
        int unloads = 0;
        int frees = 0;
        ulong observedReleases = 0;
        using NativeModuleSession session = new(new StaticModuleProvider(exports, () =>
        {
            Require(destroy(state) == 0, "native allocator state has no outstanding allocation");
            native.Dispose();
            unloads++;
        }));
        NativeAllocator allocator = new("example.instance", (
            length,
            alignment
        ) => allocate(state, length, alignment),
            (
                address,
                length,
                alignment
            ) => {
                Require(release(state, address, length, alignment) == 0, "matching native owner/extent/alignment free");
                observedReleases = releaseCount(state);
                frees++;
            }, NativeAllocatorThreadDomain.AnyThread, session);
        using NativeBuffer buffer = allocator.Allocate(13, 64);
        Require(buffer.byteLength == 13 && buffer.allocatedByteLength == 64 && buffer.alignment == 64, "logical and physical allocation extent");
        Require(allocationCount(state) == 1, "native allocator was actually called");
        ulong actual = buffer.Invoke((
            address,
            length
        ) => {
            Require((nuint)address % 64 == 0, "native allocation alignment");
            for (nuint index = 0; index < length; ++index) ((byte*)address)[index] = (byte)(index + 1);
            nint other = create();
            Require(other != 0, "independent allocation domain");
            try
            {
                Require(release(other, address, buffer.allocatedByteLength, buffer.alignment) == 2, "wrong native allocator domain is rejected");
                Require(release(state, address, buffer.allocatedByteLength + 1, buffer.alignment) == 3, "wrong native allocation extent is rejected");
            }
            finally { Require(destroy(other) == 0, "independent allocation domain cleanup"); }
            return checksum((byte*)address, length);
        });
        Require(actual == 819, "independent native weighted checksum of managed-written storage");
        session.Dispose();
        Require(unloads == 0, "module retained after public context closure");
        try { allocator.Allocate(1, 1); throw new InvalidOperationException("Closing context accepted a new allocation."); }
        catch (ObjectDisposedException) { }
        buffer.DisposeAsync().AsTask().GetAwaiter().GetResult();
        buffer.Dispose();
        Require(frees == 1 && observedReleases == 1 && unloads == 1, "one matching free precedes state destruction and module unload");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            checks = 10,
            nativeChecksum = actual,
            allocator = allocator.allocatorId,
            nativeReleases = observedReleases,
            moduleUnloads = unloads
        }));
        return 0;
    }

    private static void Require(
        bool condition,
        string check
    ) {
        if (!condition) throw new InvalidOperationException("Custom allocator check failed: " + check);
    }
}
