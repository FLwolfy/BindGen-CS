using System;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BGCS.Core.Common;
using BGCS.Core.Managed;
using BGCS.Core.Native;
using BGCS.Core.Plugins;
using BGCS.Core.Ports;
using BGCS.Core.Requests;

namespace BGCS.Tests.NeutralPlugin;

/// <summary>Names native enum members after the built-in projection without a parser or C# backend dependency.</summary>
public sealed class ManagedNamesTransform(string prefix) : IManagedApiTransform, ICacheFingerprintProvider, IDisposable
{
    /// <inheritdoc/>
    public string id => "fixture.managed-names";
    /// <inheritdoc/>
    public int priority => 5;
    /// <summary>Gets whether the session has released this service.</summary>
    public bool disposed { get; private set; }
    /// <inheritdoc/>
    public ManagedApiModel Transform(ManagedApiModel model, AnalysisContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(disposed, this);
        return model with
        {
            types = model.types.Select(type => model.interop.GetType(type.nativeType) is { category: NativeTypeCategory.Enumeration } native
                ? type with { memberNames = native.enumValues.Select(value => new ManagedMemberName(value.id, prefix + value.name.ToLowerInvariant())).ToImmutableArray() }
                : type).ToImmutableArray()
        };
    }
    /// <inheritdoc/>
    public CacheFingerprintContribution CaptureFingerprint(CacheFingerprintContext context, CancellationToken cancellationToken)
        => new(new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(prefix)))), []);
    /// <inheritdoc/>
    public void Dispose() => disposed = true;
}
