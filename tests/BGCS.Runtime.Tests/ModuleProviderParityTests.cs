using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using NativeLibrary = System.Runtime.InteropServices.NativeLibrary;

namespace BGCS.Runtime.Tests;

public sealed class ModuleProviderParityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DynamicProviderResolvesActualSystemSymbolsWithTheSelectedAssemblyScope(bool requestingAssembly)
    {
        (string library, string symbol) = SystemModule();
        using DynamicModuleProvider provider = new(library, requestingAssembly ? typeof(ModuleProviderParityTests).Assembly : null);
        Assert.True(provider.TryGetExport(symbol, out nint address));
        Assert.NotEqual(0, address);
        Assert.False(provider.TryGetExport("bgcs_missing_symbol_" + Guid.NewGuid().ToString("N"), out nint missing));
        Assert.Equal(0, missing);
        Assert.Throws<ArgumentException>(() => provider.TryGetExport(" ", out _));
        Assert.True(provider.TryGetExport(symbol, out _)); // Invalid lookup did not release the native module.
        provider.Dispose();
        provider.Dispose();
        Assert.Throws<ObjectDisposedException>(() => provider.TryGetExport(symbol, out _));
    }

    [Fact]
    public void MissingDynamicModuleFailsBeforeAProviderIsExposed()
        => Assert.Throws<DllNotFoundException>(() => new DynamicModuleProvider("bgcs_nonexistent_" + Guid.NewGuid().ToString("N")));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyNativeHandlesNeverTransferOwnership(bool ownsHandle)
        => Assert.Throws<ArgumentException>(() => DynamicModuleProvider.FromHandle(0, ownsHandle));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BorrowingAndAdoptingLoadedHandlesHaveIndependentProviderLifetimes(bool ownsHandle)
    {
        (string library, string symbol) = SystemModule();
        nint handle = NativeLibrary.Load(library);
        bool transferred = false;
        try
        {
            using DynamicModuleProvider provider = DynamicModuleProvider.FromHandle(handle, ownsHandle);
            transferred = ownsHandle;
            Assert.True(provider.TryGetExport(symbol, out nint address));
            Assert.NotEqual(0, address);
            Parallel.For(0, 32, _ => provider.Dispose());
            Assert.Throws<ObjectDisposedException>(() => provider.TryGetExport(symbol, out _));
            if (!ownsHandle)
            {
                Assert.True(NativeLibrary.TryGetExport(handle, symbol, out address));
                Assert.NotEqual(0, address);
            }
        }
        finally { if (!transferred) NativeLibrary.Free(handle); }
    }

    [Fact]
    public async Task InProgressSymbolResolutionRetainsTheProviderAcrossConcurrentSessionClosure()
    {
        using ManualResetEventSlim entered = new(false), resume = new(false);
        BlockingProvider provider = new(entered, resume);
        using NativeModuleSession session = new(provider);
        Task<IReadOnlyDictionary<string, nint>> lookup = Task.Run(() => session.ResolveRequiredSymbols(["first", "second"]));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        Task closing;
        try
        {
            closing = session.DisposeAsync().AsTask();
            Assert.False(closing.IsCompleted);
            Assert.Equal(0, Volatile.Read(ref provider.releaseCount));
            Assert.Throws<ObjectDisposedException>(() => session.ResolveRequiredSymbols(["first"]));
        }
        finally { resume.Set(); }
        IReadOnlyDictionary<string, nint> table = await lookup.WaitAsync(TimeSpan.FromSeconds(10));
        await closing.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal((nint)1, table["first"]);
        Assert.Equal((nint)2, table["second"]);
        Assert.Equal(1, Volatile.Read(ref provider.releaseCount));
    }

    [Fact]
    public void EmptySymbolSourcesCloseIdempotentlyWithoutInventingExtensionDiscovery()
    {
        int releases = 0;
        StaticModuleProvider provider = new([], () => releases++);
        using NativeModuleSession session = new(provider);
        Assert.Empty(session.ResolveRequiredSymbols([]));
        Assert.Throws<EntryPointNotFoundException>(() => session.ResolveRequiredSymbols(["missing"]));
        session.Dispose();
        session.Dispose();
        Assert.Equal(1, releases);
        Assert.Throws<ObjectDisposedException>(() => provider.TryGetExport("missing", out _));
    }

    [Fact]
    public void NameResolutionAndCustomProvidersBelongToEachSession()
    {
        List<string> firstNames = [], secondNames = [];
        int firstReleases = 0, secondReleases = 0;
        using NativeModuleSession first = Open("logical", name => "first/" + name, name =>
        {
            firstNames.Add(name);
            return new StaticModuleProvider(new Dictionary<string, nint> { ["entry"] = 11 }, () => firstReleases++);
        });
        using NativeModuleSession second = Open("logical", name => "second/" + name, name =>
        {
            secondNames.Add(name);
            return new StaticModuleProvider(new Dictionary<string, nint> { ["entry"] = 22 }, () => secondReleases++);
        });
        Assert.Equal(["first/logical"], firstNames);
        Assert.Equal(["second/logical"], secondNames);
        Assert.Equal((nint)11, first.ResolveRequiredSymbols(["entry"])["entry"]);
        Assert.Equal((nint)22, second.ResolveRequiredSymbols(["entry"])["entry"]);
        first.Dispose();
        Assert.Equal(1, firstReleases);
        Assert.Equal(0, secondReleases);
        Assert.Equal((nint)22, second.ResolveRequiredSymbols(["entry"])["entry"]);
        second.Dispose();
        Assert.Equal(1, secondReleases);

        static NativeModuleSession Open(
            string name,
            Func<string, string> resolveName,
            Func<string, INativeModuleProvider> load
        )
            => new(load(resolveName(name)));
    }

    [Fact]
    public void RevisedExternalSymbolSetsAreFrozenBeforeExposureAndDoNotMutateExistingCalls()
    {
        int firstReleases = 0, secondReleases = 0;
        List<string> names = ["first"];
        nint[] addresses = [11];
        ExternalFunctionTableProvider first = new("abi", "abi", names, addresses, new ActionLease(() => firstReleases++));
        names.Add("second");
        addresses = [22, 33];
        using ExternalFunctionTableProvider second = new("abi", "abi", names, addresses, new ActionLease(() => secondReleases++));
        Assert.True(first.TryGetExport("first", out nint original));
        Assert.Equal((nint)11, original);
        Assert.False(first.TryGetExport("second", out _));
        Assert.True(second.TryGetExport("first", out nint replacement));
        Assert.Equal((nint)22, replacement);
        Assert.True(second.TryGetExport("second", out nint added));
        Assert.Equal((nint)33, added);
        first.Dispose();
        first.Dispose();
        Assert.Equal(1, firstReleases);
        Assert.Equal(0, secondReleases);
        second.Dispose();
        Assert.Equal(1, secondReleases);
    }

    private static (string Library, string Symbol) SystemModule() => OperatingSystem.IsWindows() ? ("kernel32.dll", "GetCurrentProcessId")
        : OperatingSystem.IsMacOS() ? ("/usr/lib/libSystem.B.dylib", "getpid")
        : OperatingSystem.IsFreeBSD() ? ("libc.so.7", "getpid") : ("libc.so.6", "getpid");

    private sealed class ActionLease(Action release) : IDisposable
    {
        public void Dispose() => release();
    }

    private sealed class BlockingProvider(
        ManualResetEventSlim entered,
        ManualResetEventSlim resume
    ) : INativeModuleProvider
    {
        public int releaseCount;
        public bool TryGetExport(
            string name,
            out nint address
        ) {
            if (name == "first")
            {
                entered.Set();
                if (!resume.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Symbol lookup was not released.");
            }
            if (Volatile.Read(ref releaseCount) != 0) throw new InvalidOperationException("Provider was released during an existing symbol resolution.");
            address = name == "first" ? 1 : 2;
            return true;
        }
        public void Dispose() => Interlocked.Increment(ref releaseCount);
    }
}
