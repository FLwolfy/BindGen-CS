using System;
using System.Collections.Generic;
using System.Text.Json;
using BGCS.Runtime;
using Example.CustomAllocator;

internal static unsafe class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Supply the built native custom allocator library path.");
        // Ownership transfers to the context provider; an outer using must not bypass retained cleanup leases.
        DynamicModuleProvider native = new(args[0]);
        string[] names = ["example_allocator_create", "example_allocator_destroy", "example_allocate", "example_release",
            "example_allocations", "example_releases", "example_checksum", "example_utf8_length"];
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
        nint state = create();
        if (state == 0) { native.Dispose(); throw new OutOfMemoryException("The native allocator state could not be created."); }
        int unloads = 0;
        ulong releases = 0;
        NativeAllocator? selected = null;
        using var api = AllocatorApi.FromProvider(new StaticModuleProvider(exports, () =>
        {
            Require(destroy(state) == 0, "all temporary allocations released before native state destruction");
            native.Dispose();
            unloads++;
        }), session => selected = new NativeAllocator("example.generated-instance",
            (length, alignment) => allocate(state, length, alignment),
            (address, length, alignment) =>
            {
                Require(release(state, address, length, alignment) == 0, "matching generated temporary text free");
                releases = releaseCount(state);
            }, NativeAllocatorThreadDomain.AnyThread, session));
        ulong length = api.ExampleUtf8Length("A😀");
        Require(length == 5, "native UTF-8 byte length of a non-BMP string");
        Require(allocationCount(state) == 1 && releases == 1, "generated friendly conversion used the selected native pair");
        using NativeStringLease pending = Utf8Codec.EncodeNullTerminated("retained until cleanup", false, 64, selected);
        Require(allocationCount(state) == 2, "explicit codec shares the actual selected pair");
        api.Dispose();
        Require(unloads == 0, "pending text retains the module after context closure");
        try { _ = api.ExampleUtf8Length("closed"); throw new InvalidOperationException("Closed context accepted a native call."); }
        catch (ObjectDisposedException) { }
        Require(allocationCount(state) == 2, "closed context rejected allocation before any native callback");
        pending.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Require(releases == 2 && unloads == 1, "actual matching free precedes final module unload");
        Console.WriteLine(JsonSerializer.Serialize(new { checks = 8, nativeUtf8ByteLength = length,
            allocator = selected!.allocatorId, nativeReleases = releases, moduleUnloads = unloads }));
        return 0;
    }

    private static void Require(bool condition, string check)
    {
        if (!condition) throw new InvalidOperationException("Generated allocator check failed: " + check);
    }
}
