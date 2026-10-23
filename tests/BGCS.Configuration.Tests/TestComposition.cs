using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Hosting;
using BGCS.Core.Inputs;
using BGCS.Core.Plugins;
using BGCS.Core.Ports;
using BGCS.Core.Requests;
using BGCS.Frontend.Clang.Workers;
using BGCS.Hosting.Standard;

namespace BGCS.Configuration.Tests;

// These fixtures select their host explicitly. No production default or shared mutable facility is introduced.
internal static class TestHostPaths
{
    internal static IHostPathService Create()
    {
        HostServices host = new HostServicesProvider().Create(new(AppContext.BaseDirectory, JsonSerializer.SerializeToElement(new { })));
        // The concrete observed path service retains no file handles and has no disposal resource.
        return host.paths;
    }
}

internal static class TestInputs
{
    internal static GenerationRequest LoadRequest(
        string path,
        string? profile = null,
        CancellationToken cancellationToken = default
    )
        => LoadRequestAsync(path, profile, cancellationToken).GetAwaiter().GetResult();
    internal static Task<GenerationRequest> LoadRequestAsync(
        string path,
        string? profile = null,
        CancellationToken cancellationToken = default
    )
        => GenerationRequest.FromFile(path, profile).MaterializeAsync(TestHostPaths.Create(), cancellationToken).AsTask();
}

internal static class TestComposition
{
    internal static BindingHostBuilder UseWorkerDirectory(
        this BindingHostBuilder builder,
        string root
    )
        => builder.ReplaceFactory<IFrontend>(newId, new(newId, ExtensionPort.Frontend), context =>
        {
            HostServices host = context.RequireHostServices();
            return new FrontendWorkerClient(WorkerAssetLocator.Locate(host, root), host, TimeSpan.FromSeconds(300));
        });
    private const string newId = "bgcs.frontend.clang.worker";

    internal static BindingHostBuilder UseDetachedFrontend(
        this BindingHostBuilder builder,
        Func<IFrontend> factory
    )
        => builder.ReplaceFactory<IFrontend>(newId, new(newId, ExtensionPort.Frontend), _ => factory());
}
