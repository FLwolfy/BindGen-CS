using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Hosting;
using BGCS.Core.Inputs;
using BGCS.Core.Plugins;
using BGCS.Core.Ports;
using BGCS.Core.Requests;
using BGCS.Interop.DotNet;
using BGCS.Targets.Desktop.Targeting;
using BGCS.Core.Targets;
using BGCS.Frontend.Clang.Workers;
using BGCS.Hosting.Standard;

namespace BGCS.Tests;

// These fixtures select their host explicitly. No production default or shared mutable facility is introduced.
internal static class TestHostPaths
{
    internal static HostServices CreateHost() => new HostServicesProvider().Create(new(AppContext.BaseDirectory, JsonSerializer.SerializeToElement(new { })));
    internal static IHostPathService Create()
    {
        HostServices host = new HostServicesProvider().Create(new(AppContext.BaseDirectory, JsonSerializer.SerializeToElement(new { })));
        // The concrete observed path service retains no file handles and has no disposal resource.
        return host.paths;
    }
}

internal static class TestInputs
{
    internal static GenerationRequest LoadRequest(string path, string? profile = null, CancellationToken cancellationToken = default)
        => LoadRequestAsync(path, profile, cancellationToken).GetAwaiter().GetResult();
    internal static Task<GenerationRequest> LoadRequestAsync(string path, string? profile = null, CancellationToken cancellationToken = default)
        => GenerationRequest.FromFile(path, profile).MaterializeAsync(TestHostPaths.Create(), cancellationToken).AsTask();
}

internal static class TestComposition
{
    internal static string HostTargetId() => new TargetResolver().Resolve(new(new("host"))).targetId.value;

    internal static ManagedRuntimeProfile ResolveRuntime(NativeTargetDescriptor target)
        => new DotNetRuntimeProvider().TryResolve(new(new("dotnet.coreclr"), target), out ManagedRuntimeProfile? profile) ? profile
            : throw new NotSupportedException("The fixture target has no established desktop execution profile.");
    internal static int PointerSize(NativeTargetDescriptor target) => target.architectureId switch
    { "x64" or "arm64" => 8, "x86" => 4, _ => throw new NotSupportedException("This scalar fixture has no pointer model for " + target.architectureId) };
    internal static BindingHostBuilder UseWorkerDirectory(this BindingHostBuilder builder, string root)
        => builder.ReplaceFactory<IFrontend>(newId, new(newId, ExtensionPort.Frontend), context =>
        {
            HostServices host = context.RequireHostServices();
            return new FrontendWorkerClient(WorkerAssetLocator.Locate(host, root), host, TimeSpan.FromSeconds(300));
        });
    private const string newId = "bgcs.frontend.clang.worker";

    internal static BindingHostBuilder UseDetachedFrontend(this BindingHostBuilder builder, Func<IFrontend> factory)
        => builder.ReplaceFactory<IFrontend>(newId, new(newId, ExtensionPort.Frontend), _ => factory());

    internal static BindingHostBuilder ObserveWorkerParses(this BindingHostBuilder builder, string root, ConcurrentQueue<SourceLanguage> parses)
        => builder.ReplaceFactory<IFrontend>(newId, new(newId, ExtensionPort.Frontend), context =>
        {
            HostServices host = context.RequireHostServices();
            return new RecordingWorker(new FrontendWorkerClient(WorkerAssetLocator.Locate(host, root), host, TimeSpan.FromSeconds(300)), parses);
        });

    private sealed class RecordingWorker(IIsolatedFrontend worker, ConcurrentQueue<SourceLanguage> parses) : IIsolatedFrontend
    {
        public string parserIdentity => worker.parserIdentity;
        public FrontendExecutionIdentity CaptureExecutionIdentity(CancellationToken cancellationToken) => worker.CaptureExecutionIdentity(cancellationToken);
        public ValueTask<WorkerPluginCapture> CapturePluginInputsAsync(FrontendRequest request, CancellationToken cancellationToken)
            => worker.CapturePluginInputsAsync(request, cancellationToken);
        public ValueTask<FrontendPreparation> PrepareAsync(FrontendRequest request, CancellationToken cancellationToken) => worker.PrepareAsync(request, cancellationToken);
        public ValueTask<FrontendResult> ParseAsync(FrontendRequest request, CancellationToken cancellationToken)
        {
            parses.Enqueue(request.language);
            return worker.ParseAsync(request, cancellationToken);
        }
    }
}
