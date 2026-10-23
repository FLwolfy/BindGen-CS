using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Requests;
using BGCS.Core.Targets;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Validation;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class EventLoopLifecycleTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task ExplicitEventLoopProtocolsEmitCompilableDeferredShutdownAndKeepFinalGuards(BindingImportMode mode, bool asynchronous)
    {
        ManagedApiModel model = Model(asynchronous);
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native", sink) { importMode = mode, includeRuntimeSource = asynchronous,
            managedRuntime = new("nonblocking-test", new ManagedRuntimeId("fixture.runtime"), NativeModuleMechanism.ExternalFunctionTable,
                [mode], requiresStaticCallbackThunks: true, allowsBlockingDisposal: false) };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        Assert.Empty(plan.callbackOmissions);
        if (asynchronous) Assert.True(Assert.Single(plan.asyncOperations).nonBlockingShutdown);
        else Assert.True(Assert.Single(plan.retainedCallbacks).nonBlockingShutdown);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        string source = sink.sources.Single(item => item.Key.StartsWith(asynchronous ? "Async/" : "Callbacks/", StringComparison.Ordinal)
            && item.Value.Contains("StopAndJoinAsync", StringComparison.Ordinal)).Value;
        Assert.DoesNotContain("Task.Run", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Monitor.Wait", source, StringComparison.Ordinal);
        Assert.DoesNotContain(CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>(),
            method => method.Modifiers.Any(token => token.RawKind == (int)SyntaxKind.AsyncKeyword));
        foreach ((string before, string after) in new[]
        {
            ("Task.WhenAll(ready, drained)", "Task.CompletedTask"),
            ("dispatcher.VerifyAccess();", ""),
            ("registration!.DisposeAsync()", "global::System.Threading.Tasks.ValueTask.CompletedTask")
        })
        {
            string changed = source.Replace(before, after, StringComparison.Ordinal);
            Assert.NotEqual(source, changed);
            Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan,
                sink.sources.Select(item => (item.Key, item.Value == source ? changed : item.Value)), "native"));
        }
        await ValueReceiverTests.CompileWithSdkAsync(sink, string.Empty, sourceRuntime: asynchronous);
    }

    [Theory]
    [InlineData("unknown-stop")]
    [InlineData("blocking-stop")]
    [InlineData("any-thread")]
    [InlineData("caller-thread")]
    [InlineData("unknown-domain")]
    [InlineData("no-new-only")]
    [InlineData("unknown-cancel")]
    [InlineData("blocking-cancel")]
    public void IncompleteOrContradictoryEventLoopProtocolsPreserveTheRawAbi(string fault)
    {
        ManagedApiModel model = Model(asynchronous: true);
        InteropFunction start = model.interop.functions[0];
        CallbackContract callback = start.parameters[0].callback!;
        callback = fault switch
        {
            "unknown-stop" => callback with { registration = callback.registration! with { unregisterIsNonBlocking = null } },
            "blocking-stop" => callback with { registration = callback.registration! with { unregisterIsNonBlocking = false } },
            "any-thread" => callback with { threading = NativeCallbackThreading.AnyThread },
            "caller-thread" => callback with { threading = NativeCallbackThreading.CallerThread },
            "unknown-domain" => callback with { registration = callback.registration! with { releaseThreadDomain = "unknown" } },
            "no-new-only" => callback with { unregisterQuiescence = NativeQuiescenceKind.NoNewInvocations },
            _ => callback
        };
        start = start with { parameters = start.parameters.SetItem(0, start.parameters[0] with { callback = callback }), asynchronous = start.asynchronous! with
        { cancellationIsNonBlocking = fault == "unknown-cancel" ? null : fault != "blocking-cancel" } };
        InteropModule module = new(model.interop.libraryId, model.interop.targetId, model.interop.pointerSize, model.interop.types,
            model.interop.functions.SetItem(0, start), []) { byteOrder = model.interop.byteOrder };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model with { interop = module }, new("native", new NeutralBackendTests.MemorySink())
        { managedRuntime = new("event-loop-test", new ManagedRuntimeId("fixture.runtime"), NativeModuleMechanism.ExternalFunctionTable, [BindingImportMode.FunctionTable], true, false) });
        Assert.Empty(plan.asyncOperations);
        Assert.Single(plan.callbackOmissions);
        Assert.Equal(3, plan.methods.Length);
    }

    private static ManagedApiModel Model(bool asynchronous)
    {
        ManagedApiModel model = asynchronous ? AsyncOperationTests.Model() : RetainedCallbackTests.Model();
        InteropFunction start = model.interop.functions[0];
        CallbackContract callback = start.parameters[0].callback!;
        callback = callback with { threading = NativeCallbackThreading.EventLoop,
            registration = callback.registration! with { unregisterIsNonBlocking = true, releaseThreadDomain = "eventLoop" } };
        start = start with { parameters = start.parameters.SetItem(0, start.parameters[0] with { callback = callback }),
            asynchronous = start.asynchronous is { } protocol ? protocol with { cancellationIsNonBlocking = true } : null };
        return model with { interop = new(model.interop.libraryId, model.interop.targetId, model.interop.pointerSize, model.interop.types,
            model.interop.functions.SetItem(0, start), []) { byteOrder = model.interop.byteOrder } };
    }
}
