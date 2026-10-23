using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BGCS.Core;
using BGCS.Core.Calls;
using BGCS.Core.Common;
using BGCS.Core.Native;
using BGCS.Core.Plugins;
using BGCS.Core.Ports;
using BGCS.Core.Requests;
using BGCS.Core.Targets;
using BGCS.Interop.DotNet;

namespace BGCS.Examples.CustomCallAbi;

/// <summary>Composes an explicit integer-only policy over the existing .NET calling implementation.</summary>
/// <remarks>This example restricts an implemented runtime; it does not establish a new platform ABI.</remarks>
public sealed class IntegerOnlyPlugin : IBindingPlugin, ICacheFingerprintProvider
{
    /// <inheritdoc />
    public PluginDescriptor descriptor => new("bgcs.examples.integer-only", "4.1.0",
        [new("example.integer-runtime", ExtensionPort.ManagedRuntimeProvider), new("example.integer-abi", ExtensionPort.CallAbiProvider)]);
    /// <inheritdoc />
    public void Configure(
        IExtensionRegistryBuilder registry,
        PluginConfiguration configuration
    ) {
        registry.RegisterFactory<IManagedRuntimeProvider>("example.integer-runtime", _ => new IntegerOnlyRuntime());
        registry.RegisterFactory<ICallAbiProvider>("example.integer-abi", _ => new IntegerOnlyCallAbi());
    }
    /// <inheritdoc />
    public CacheFingerprintContribution CaptureFingerprint(
        CacheFingerprintContext context,
        CancellationToken cancellationToken
    )
        => Identity(cancellationToken);
    internal static CacheFingerprintContribution Identity(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string identity = typeof(IntegerOnlyPlugin).Assembly.ManifestModule.ModuleVersionId.ToString("D") + "/"
            + typeof(DotNetCallAbi).Assembly.ManifestModule.ModuleVersionId.ToString("D");
        return new(new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))), []);
    }
}

/// <summary>Admits only an explicitly selected FunctionTable policy with caller-supplied symbols.</summary>
public sealed class IntegerOnlyRuntime : IManagedRuntimeProvider, ICacheFingerprintProvider
{
    /// <summary>Gets the explicit runtime policy selected by this sample's configuration.</summary>
    public const string RUNTIME_ID = "example.coreclr-integer-only";
    /// <inheritdoc />
    public bool TryResolve(
        ManagedRuntimeRequest request,
        [NotNullWhen(true)] out ManagedRuntimeProfile? profile
    ) {
        profile = null;
        if (request.runtimeId.value != RUNTIME_ID) return false;
        if (!new DotNetRuntimeProvider().TryResolve(new(new("dotnet.coreclr"), request.target), out ManagedRuntimeProfile? implemented))
            return false;
        profile = implemented with
        {
            id = "example/integer-only/1/" + implemented.id,
            runtimeId = new(RUNTIME_ID),
            moduleMechanism = NativeModuleMechanism.ExternalFunctionTable,
            importModes = [BindingImportMode.FunctionTable]
        };
        return true;
    }
    /// <inheritdoc />
    public CacheFingerprintContribution CaptureFingerprint(
        CacheFingerprintContext context,
        CancellationToken cancellationToken
    )
        => IntegerOnlyPlugin.Identity(cancellationToken);
}

/// <summary>Requires exact forward integer signatures and delegates their physical classification to the implemented ABI.</summary>
public sealed class IntegerOnlyCallAbi : ICallAbiProvider, ICacheFingerprintProvider
{
    /// <inheritdoc />
    public CallAbiAssessment Assess(CallAbiRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.runtime.runtimeId.value != IntegerOnlyRuntime.RUNTIME_ID)
            return new(request.runtime.callAbi, NativeCallDisposition.NotApplicable, "Another provider owns this runtime.");
        if (!new IntegerOnlyRuntime().TryResolve(new(request.runtime.runtimeId, request.target), out ManagedRuntimeProfile? expected))
            return new(request.runtime.callAbi, NativeCallDisposition.Unsupported, "The existing runtime cannot establish this target.");
        if (request.signature.direction != NativeCallDirection.Forward
            || request.signature.parameterTypes.Append(request.signature.returnType).Any(id => !Integer(id)))
            return new(request.runtime.callAbi, NativeCallDisposition.Unsupported, "This sample implements only fixed forward int32 signatures; pointers, aggregates and reverse calls require another explicit provider.");
        return DotNetCallAbi.Assess(request, expected);

        bool Integer(StableId id)
        {
            var type = request.module.GetType(id);
            while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
                type = request.module.GetType(type.elementType!.Value);
            return type.category == NativeTypeCategory.Primitive && type.primitiveKind == "Int" && type.size == 4 && type.alignment == 4 && type.isSigned == true;
        }
    }
    /// <inheritdoc />
    public CacheFingerprintContribution CaptureFingerprint(
        CacheFingerprintContext context,
        CancellationToken cancellationToken
    )
        => IntegerOnlyPlugin.Identity(cancellationToken);
}
