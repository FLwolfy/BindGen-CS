using System;
using BGCS.Core.Hosting;
using BGCS.Core.Plugins;
using BGCS.Core.Ports;
using BGCS.Core.Targets;
using BGCS.Frontend.Clang.Workers;
using BGCS.Hosting.Standard;
using BGCS.Interop.DotNet;
using BGCS.Targets.Desktop;

namespace BGCS.Composition;

internal static class StandardBindingComposition
{
    internal static BindingHostBuilder CreateBuilder() => BindingHost.CreateBuilder()
        .RegisterFactory<IHostServicesProvider>(new("bgcs.hosts.observed", ExtensionPort.HostServicesProvider),
            static _ => new HostServicesProvider())
        .RegisterFactory<IFrontend>(new("bgcs.frontend.clang.worker", ExtensionPort.Frontend), static context =>
        {
            HostServices host = context.RequireHostServices();
            return new FrontendWorkerClient(WorkerAssetLocator.Locate(host), host, TimeSpan.FromSeconds(300));
        })
        .RegisterFactory<INativeTargetProvider>(new("bgcs.targets.desktop", ExtensionPort.NativeTargetProvider),
            static _ => new DesktopTargetProvider())
        .RegisterFactory<IManagedRuntimeProvider>(new("dotnet.execution", ExtensionPort.ManagedRuntimeProvider),
            static _ => new DotNetRuntimeProvider())
        .RegisterFactory<ICallAbiProvider>(new("dotnet.call-abi", ExtensionPort.CallAbiProvider),
            static _ => new DotNetCallAbiProvider());
}
