using System;
using System.Threading;
using BGCS.Core.Calls;
using BGCS.Core.Common;
using BGCS.Core.Plugins;
using BGCS.Core.Ports;
using BGCS.Core.Targets;

namespace BGCS.Interop.DotNet;

/// <summary>Assesses direct CoreCLR calls using the extension's independently resolved desktop profile.</summary>
public sealed class DotNetCallAbiProvider : ICallAbiProvider, ICacheFingerprintProvider
{
    // Only pure profile facts are retained for the same immutable input objects.
    // Every assessment still checks and classifies its own module and signature.
    private ProfileBinding? m_binding;

    /// <inheritdoc />
    public CacheFingerprintContribution CaptureFingerprint(
        CacheFingerprintContext context,
        CancellationToken cancellationToken
    )
        => ExtensionFingerprint.Capture(GetType(), cancellationToken);

    /// <inheritdoc />
    public CallAbiAssessment Assess(CallAbiRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.runtime.runtimeId.value != "dotnet.coreclr")
            return new(request.runtime.callAbi, NativeCallDisposition.NotApplicable, "This provider implements CoreCLR; select the extension that owns the requested runtime.");
        try
        {
            ProfileBinding? binding = Volatile.Read(ref m_binding);
            if (binding is not null && ReferenceEquals(binding.target, request.target) && ReferenceEquals(binding.runtime, request.runtime))
                return DotNetCallAbi.AssessDerivedProfile(request, binding.expectedRuntime, binding.profileDigest);
            if (new DotNetRuntimeProvider().TryResolve(new(request.runtime.runtimeId, request.target), out ManagedRuntimeProfile? expected))
            {
                CallAbiAssessment result = DotNetCallAbi.Assess(request, expected);
                if (result.disposition == NativeCallDisposition.Direct && result.plan is { } plan)
                    Volatile.Write(ref m_binding, new ProfileBinding(request.target, request.runtime, expected, plan.runtimeProfileDigest));
                return result;
            }
        }
        catch (NotSupportedException failure)
        {
            return new(request.runtime.callAbi, NativeCallDisposition.Unsupported, failure.Message);
        }
        return new(request.runtime.callAbi, NativeCallDisposition.Unsupported, "The CoreCLR execution model could not be established.");
    }

    private sealed record ProfileBinding(
        NativeTargetDescriptor target,
        ManagedRuntimeProfile runtime,
        ManagedRuntimeProfile expectedRuntime,
        ContentDigest profileDigest
    );
}
