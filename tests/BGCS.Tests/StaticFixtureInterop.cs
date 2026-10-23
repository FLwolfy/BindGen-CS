using System;
using System.Diagnostics.CodeAnalysis;
using BGCS.Core.Calls;
using BGCS.Core.Plugins;
using BGCS.Core.Ports;
using BGCS.Core.Targets;
using BGCS.Interop.DotNet;

namespace BGCS.Tests;

// The test consumer is CoreCLR on the actual desktop ABI. It supplies exports
// through the generated static table instead of claiming NativeAOT/browser certification.
internal static class StaticFixtureInterop
{
    internal static BindingHostBuilder UseFixtureStaticRuntime(this BindingHostBuilder builder)
        => builder.RegisterFactory<IManagedRuntimeProvider>(new("fixture.static.runtime", ExtensionPort.ManagedRuntimeProvider), _ => new Runtime())
            .RegisterFactory<ICallAbiProvider>(new("fixture.static.call-abi", ExtensionPort.CallAbiProvider), _ => new Calls());

    private sealed class Runtime : IManagedRuntimeProvider
    {
        public bool TryResolve(
            ManagedRuntimeRequest request,
            [NotNullWhen(true)] out ManagedRuntimeProfile? profile
        ) {
            profile = null;
            if (request.runtimeId.value != "fixture.static") return false;
            NativeTargetDescriptor target = request.target;
            NativeTargetDescriptor desktop = new(target.targetId, target.platformId, target.architectureId, target.abiId, target.triple,
                target.toolchain, new(false, true), target.byteOrder);
            if (!new DotNetRuntimeProvider().TryResolve(new(new("dotnet.coreclr"), desktop), out ManagedRuntimeProfile? dynamicProfile)) return false;
            profile = dynamicProfile with
            {
                id = target.targetId.value + "/fixture.static/v1",
                runtimeId = new("fixture.static"),
                moduleMechanism = NativeModuleMechanism.StaticAdapter,
                requiresStaticCallbackThunks = true
            };
            return true;
        }
    }

    private sealed class Calls : ICallAbiProvider
    {
        public CallAbiAssessment Assess(CallAbiRequest request)
        {
            if (!new Runtime().TryResolve(new(request.runtime.runtimeId, request.target), out ManagedRuntimeProfile? expected))
                return new(request.runtime.callAbi, NativeCallDisposition.NotApplicable, "This fixture owns only its explicit static-table execution model.");
            return DotNetCallAbi.Assess(request, expected);
        }
    }
}
