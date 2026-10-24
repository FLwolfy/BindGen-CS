using System;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BGCS.Core.Calls;
using BGCS.Core.Common;
using BGCS.Core.Plugins;
using BGCS.Core.Ports;
using BGCS.Core.Targets;
using BGCS.Interop.DotNet;

namespace BGCS.Extension.NativeAot;

/// <summary>Resolves explicitly requested NativeAOT execution for implemented desktop target families.</summary>
public sealed class NativeAotRuntimeProvider : IManagedRuntimeProvider, ICacheFingerprintProvider
{
    /// <inheritdoc />
    public CacheFingerprintContribution CaptureFingerprint(
        CacheFingerprintContext context,
        CancellationToken cancellationToken
    )
        => NativeAotFingerprint.Capture(GetType(), cancellationToken);

    /// <inheritdoc />
    public bool TryResolve(
        ManagedRuntimeRequest request,
        [NotNullWhen(true)] out ManagedRuntimeProfile? profile
    ) {
        ArgumentNullException.ThrowIfNull(request);
        profile = null;
        if (request.runtimeId.value != "dotnet.native-aot") return false;
        if (request.target.architectureId is not ("x64" or "arm64"))
            throw new NotSupportedException("The NativeAOT extension establishes only its implemented 64-bit desktop execution families.");
        if (!new DotNetRuntimeProvider().TryResolve(new(new("dotnet.coreclr"), request.target), out ManagedRuntimeProfile? desktop))
            return false;
        profile = desktop with
        {
            id = request.target.targetId.value + "/dotnet.native-aot/v1",
            runtimeId = request.runtimeId,
            requiresStaticCallbackThunks = true
        };
        return true;
    }
}

/// <summary>Assesses NativeAOT calls with the shared, independently implemented .NET carrier algorithms.</summary>
public sealed class NativeAotCallAbiProvider : ICallAbiProvider, ICacheFingerprintProvider
{
    /// <inheritdoc />
    public CacheFingerprintContribution CaptureFingerprint(
        CacheFingerprintContext context,
        CancellationToken cancellationToken
    )
        => NativeAotFingerprint.Capture(GetType(), cancellationToken);

    /// <inheritdoc />
    public CallAbiAssessment Assess(CallAbiRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.runtime.runtimeId.value != "dotnet.native-aot")
            return new(request.runtime.callAbi, NativeCallDisposition.NotApplicable, "The requested runtime belongs to another extension.");
        try
        {
            if (new NativeAotRuntimeProvider().TryResolve(new(request.runtime.runtimeId, request.target), out ManagedRuntimeProfile? expected))
                return DotNetCallAbi.Assess(request, expected);
        }
        catch (NotSupportedException failure) { return new(request.runtime.callAbi, NativeCallDisposition.Unsupported, failure.Message); }
        return new(request.runtime.callAbi, NativeCallDisposition.Unsupported, "The NativeAOT execution model could not be established.");
    }
}

/// <summary>Registers optional NativeAOT runtime and call-ABI providers without an engine dependency.</summary>
public sealed class NativeAotPlugin : IBindingPlugin, ICacheFingerprintProvider
{
    /// <inheritdoc />
    public PluginDescriptor descriptor => new("bgcs.extension.native-aot", "4.1.1", [
        new("native-aot.execution", ExtensionPort.ManagedRuntimeProvider), new("native-aot.call-abi", ExtensionPort.CallAbiProvider)]);
    /// <inheritdoc />
    public CacheFingerprintContribution CaptureFingerprint(
        CacheFingerprintContext context,
        CancellationToken cancellationToken
    )
        => NativeAotFingerprint.Capture(GetType(), cancellationToken);
    /// <inheritdoc />
    public void Configure(
        IExtensionRegistryBuilder registry,
        PluginConfiguration configuration
    ) {
        registry.RegisterFactory<IManagedRuntimeProvider>("native-aot.execution", static _ => new NativeAotRuntimeProvider());
        registry.RegisterFactory<ICallAbiProvider>("native-aot.call-abi", static _ => new NativeAotCallAbiProvider());
    }
}

internal static class NativeAotFingerprint
{
    internal static CacheFingerprintContribution Capture(
        Type service,
        CancellationToken cancellationToken
    ) {
        cancellationToken.ThrowIfCancellationRequested();
        string state = service.FullName + "/v1/" + service.Assembly.ManifestModule.ModuleVersionId.ToString("D")
            + "/" + typeof(DotNetCallAbi).Assembly.ManifestModule.ModuleVersionId.ToString("D");
        return new(new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(state)))), []);
    }
}
