using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace BGCS.Runtime.Tests;

public sealed class CallbackRegistryTests
{
    [Fact]
    public unsafe void StaticThunkRejectsRetiredUserDataWithoutReadingFreedHandlesOrNewState()
    {
        delegate* unmanaged[Cdecl]<nint, int, int> thunk = &Invoke;
        State first = new(10), second = new(100);
        CallbackRegistry<State>.Registration registration = CallbackRegistry<State>.Register(first);
        nint retired = registration.token;
        Assert.Equal(42, thunk(retired, 32));
        registration.Dispose();
        registration.Dispose();
        using CallbackRegistry<State>.Registration replacement = CallbackRegistry<State>.Register(second);
        Assert.NotEqual(retired, replacement.token);
        Assert.Equal(-1, thunk(retired, 32));
        Assert.Equal(-1, thunk(0, 32));
        Assert.Equal(132, thunk(replacement.token, 32));
        Assert.Equal(1, first.calls);
        Assert.Equal(1, second.calls);
    }

    [Fact]
    public async Task ConcurrentRegistrationsAndRepeatedRetirementKeepIdentitiesIndependent()
    {
        ConcurrentBag<CallbackRegistry<State>.Registration> registrations = [];
        await Parallel.ForEachAsync(Enumerable.Range(0, 512), (value, _) =>
        {
            var registration = CallbackRegistry<State>.Register(new(value));
            Assert.True(CallbackRegistry<State>.TryGet(registration.token, out State? state));
            Assert.Equal(value, state!.offset);
            registrations.Add(registration);
            return ValueTask.CompletedTask;
        });
        Assert.Equal(512, registrations.Select(registration => registration.token).Distinct().Count());
        await Parallel.ForEachAsync(registrations.Concat(registrations), (registration, _) =>
        { registration.Dispose(); return ValueTask.CompletedTask; });
        foreach (var registration in registrations) Assert.False(CallbackRegistry<State>.TryGet(registration.token, out _));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Invoke(nint token, int value)
    {
        if (!CallbackRegistry<State>.TryGet(token, out State? state)) return -1;
        Interlocked.Increment(ref state.calls);
        return state.offset + value;
    }

    private sealed class State(int value)
    {
        internal readonly int offset = value;
        internal int calls;
    }
}
