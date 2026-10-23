using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Core.Ports;
using BGCS.Core.Requests;
using BGCS.Core.Targets;
using BGCS.Core.Plugins;
using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace BGCS.Tests;

public sealed class BindingHostTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StandaloneAnalysis_TracksInputsBeforeResolutionAndAfterServiceDisposal(bool providerMutation)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-analysis-inputs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string input = Path.Combine(temporary, "external.dat");
            File.WriteAllText(input, "before");
            BindingHostBuilder builder = BindingGenerator.CreateBuilder().UseDetachedFrontend(() =>
                providerMutation ? new ProbeFrontend(() => { }) : new DeclaredFrontend(input));
            if (providerMutation) builder.RegisterFactory<INativeTargetProvider>(new("declared-inputs", ExtensionPort.NativeTargetProvider), _ => new DeclaredProvider(input));
            await using BindingHost host = builder.Build();
            AnalysisResult result = await host.AnalyzeAsync(Request() with { baseDirectory = temporary });
            Assert.False(result.success);
            Assert.Contains(result.diagnostics, diagnostic => diagnostic.code == "BGCS-ANALYSIS-INPUTS" && diagnostic.message.Contains("external inputs changed", StringComparison.Ordinal));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Fact]
    public async Task InvalidAnalysisInputBase_ReleasesSessionAdmissionBeforeHostShutdown()
    {
        BindingHost host = BindingGenerator.CreateBuilder().UseDetachedFrontend(() => new ProbeFrontend(() => { })).Build();
        AnalysisResult result = await host.AnalyzeAsync(Request() with { baseDirectory = null! });
        Assert.False(result.success);
        await host.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class DeclaredProvider(string input) : INativeTargetProvider, IExternalInputProvider
    {
        public ImmutableArray<DeclaredExternalInput> GetExternalInputs(
            ExternalInputContext context,
            CancellationToken cancellationToken
        ) => [new(input, ExternalInputKind.File)];
        public bool TryResolve(
            NativeTargetRequest request,
            [NotNullWhen(true)] out NativeTargetDescriptor? target
        ) {
            File.WriteAllText(input, "after!");
            target = null;
            return false;
        }
    }

    private sealed class DeclaredFrontend(string input) : IFrontend, IExternalInputProvider, IDisposable
    {
        public ValueTask<FrontendPreparation> PrepareAsync(
            FrontendRequest request,
            CancellationToken cancellationToken
        ) {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new FrontendPreparation(request, new([], []), []));
        }

        public ImmutableArray<DeclaredExternalInput> GetExternalInputs(
            ExternalInputContext context,
            CancellationToken cancellationToken
        ) => [new(input, ExternalInputKind.File)];
        public ValueTask<FrontendResult> ParseAsync(
            FrontendRequest request,
            CancellationToken cancellationToken
        )
            => new ProbeFrontend(() => { }).ParseAsync(request, cancellationToken);
        public void Dispose() => File.WriteAllText(input, "after!");
    }

    [Fact]
    public async Task ExplicitUninstalledCompilerHint_DoesNotPreventParsing()
    {
        bool created = false;
        string hint = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing-compiler");
        await using BindingHost host = BindingGenerator.CreateBuilder().UseDetachedFrontend(() => { created = true; return new ProbeFrontend(() => { }); }).Build();
        AnalysisResult result = await host.AnalyzeAsync(Request() with { toolchain = new NativeToolchainDescriptor(compilerPath: hint) });
        Assert.True(result.success, result.GetDiagnosticSummary());
        Assert.True(created);
        Assert.Equal(hint, result.target!.toolchain.compilerPath);
    }

    [Fact]
    public async Task Sessions_CreateAndDisposeIndependentFrontendServices()
    {
        int created = 0;
        int disposed = 0;
        await using BindingHost host = BindingGenerator.CreateBuilder().UseDetachedFrontend(() =>
        {
            Interlocked.Increment(ref created);
            return new ProbeFrontend(() => Interlocked.Increment(ref disposed));
        }).Build();
        AnalysisResult first = await host.AnalyzeAsync(Request());
        AnalysisResult second = await host.AnalyzeAsync(Request());
        Assert.True(first.success);
        Assert.True(second.success);
        Assert.Equal(2, created);
        Assert.Equal(2, disposed);
    }

    [Fact]
    public async Task ClosingHost_CancelsAndDrainsAnActiveFrontendSession()
    {
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int disposed = 0;
        BindingHost host = BindingGenerator.CreateBuilder()
            .UseDetachedFrontend(() => new HangingFrontend(entered, () => Interlocked.Increment(ref disposed))).Build();
        Task analyze = host.AnalyzeAsync(Request());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await host.DisposeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => analyze);
        Assert.Equal(1, disposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => host.AnalyzeAsync(Request()));
        await host.DisposeAsync();
    }

    [Fact]
    public async Task FrontendFailure_BecomesAnOwnedSessionDiagnostic()
    {
        await using BindingHost host = BindingGenerator.CreateBuilder()
            .UseDetachedFrontend(() => throw new IOException("Parser assets unavailable.")).Build();
        AnalysisResult result = await host.AnalyzeAsync(Request());
        Assert.False(result.success);
        Assert.Null(result.module);
        Assert.Contains("Parser assets unavailable", result.GetDiagnosticSummary());
    }

    [Fact]
    public void RequestCreation_CopiesMutableHeadersAndChoosesLanguageDefaults()
    {
        string[] inputs = ["one.h"];
        AnalysisRequest c = AnalysisRequest.FromFiles(inputs);
        inputs[0] = "changed.h";
        Assert.Equal("one.h", Assert.Single(c.headers));
        Assert.Equal("c17", c.languageStandard);
        Assert.Equal("c++23", AnalysisRequest.FromFiles(inputs, SourceLanguage.Cpp).languageStandard);
    }

    [Fact]
    public void DuplicatePrimaryFrontendFactory_IsRejected()
    {
        BindingHostBuilder builder = BindingGenerator.CreateBuilder()
            .RegisterFactory<IFrontend>(new("fixture.duplicate-frontend", ExtensionPort.Frontend), _ => new ProbeFrontend(() => { }));
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    private static AnalysisRequest Request() => AnalysisRequest.FromFiles(["native.h"], baseDirectory: AppContext.BaseDirectory) with
    {
        targetId = "host"
    };

    private sealed class ProbeFrontend(Action dispose) : IFrontend, IDisposable
    {
        public ValueTask<FrontendPreparation> PrepareAsync(
            FrontendRequest request,
            CancellationToken cancellationToken
        ) {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new FrontendPreparation(request, new([], []), []));
        }

        public ValueTask<FrontendResult> ParseAsync(
            FrontendRequest request,
            CancellationToken cancellationToken
        ) {
            NativeModule module = new(request.target.targetId.value, request.target.triple, TestComposition.PointerSize(request.target), [], [], []) { language = request.language };
            return ValueTask.FromResult(new FrontendResult(module, new SourceSnapshot([], [])));
        }
        public void Dispose() => dispose();
    }

    private sealed class HangingFrontend(
        TaskCompletionSource entered,
        Action dispose
    ) : IFrontend, IDisposable
    {
        public ValueTask<FrontendPreparation> PrepareAsync(
            FrontendRequest request,
            CancellationToken cancellationToken
        ) {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new FrontendPreparation(request, new([], []), []));
        }

        public async ValueTask<FrontendResult> ParseAsync(
            FrontendRequest request,
            CancellationToken cancellationToken
        ) {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("An uncanceled hanging frontend unexpectedly completed.");
        }
        public void Dispose() => dispose();
    }

    [Fact]
    public async Task NativeTransforms_UseFrozenPriorityOrderAndContractsOnlyInputs()
    {
        List<string> order = [];
        BindingHostBuilder builder = BindingGenerator.CreateBuilder()
            .UseDetachedFrontend(() => new ProbeFrontend(() => { }))
            .RegisterFactory<INativeModuleTransform>(new("second", ExtensionPort.NativeModuleTransform, 0), _ => new ProbeTransform("second", 0, order, () => { }))
            .RegisterFactory<INativeModuleTransform>(new("first", ExtensionPort.NativeModuleTransform, 1), _ => new ProbeTransform("first", 1, order, () => { }));
        await using BindingHost host = builder.Build();
        builder.RegisterFactory<INativeModuleTransform>(new("later", ExtensionPort.NativeModuleTransform, 99), _ => new ProbeTransform("later", 99, order, () => { }));
        AnalysisResult result = await host.AnalyzeAsync(Request());
        Assert.True(result.success);
        Assert.Equal(new[] { "first", "second" }, order);
        Assert.Throws<InvalidOperationException>(() => builder.RegisterFactory<INativeModuleTransform>(new("first", ExtensionPort.NativeModuleTransform, 1), _ => null!));
    }

    [Fact]
    public async Task ServiceCleanupFailure_StillReleasesRemainingServicesAndReturnsFailureDiagnostics()
    {
        int released = 0;
        await using BindingHost host = BindingGenerator.CreateBuilder()
            .UseDetachedFrontend(() => new ProbeFrontend(() => released++))
            .RegisterFactory<INativeModuleTransform>(new("normal", ExtensionPort.NativeModuleTransform, 1), _ => new ProbeTransform("normal", 1, [], () => released++))
            .RegisterFactory<INativeModuleTransform>(new("fault", ExtensionPort.NativeModuleTransform, 0), _ => new ProbeTransform("fault", 0, [], () => throw new IOException("cleanup failure"))).Build();
        AnalysisResult result = await host.AnalyzeAsync(Request());
        Assert.False(result.success);
        Assert.Equal(2, released);
        Assert.Contains("cleanup failure", result.GetDiagnosticSummary());
    }

    private sealed class ProbeTransform(
        string identity,
        int ordering,
        List<string> trace,
        Action release
    ) : INativeModuleTransform, IDisposable
    {
        public string id => identity;
        public int priority => ordering;
        public NativeModule Transform(
            NativeModule module,
            AnalysisContext context,
            CancellationToken cancellationToken
        ) {
            Assert.Equal(module.targetId, context.target.targetId.value);
            trace.Add(id);
            return module;
        }
        public void Dispose() => release();
    }
}
