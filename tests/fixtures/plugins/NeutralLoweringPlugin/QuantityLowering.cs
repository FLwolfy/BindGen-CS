using System;
using System.Threading;
using BGCS.Core.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Native;
using BGCS.Core.Ports;
using BGCS.Core.Requests;

namespace BGCS.Tests.NeutralPlugin;

/// <summary>Demonstrates a detached custom numeric domain conversion without frontend or host references.</summary>
public sealed class QuantityLowering : INativeTypeLowering, IDisposable
{
    /// <inheritdoc/>
    public string id => "fixture.quantity";
    /// <inheritdoc/>
    public int priority => 20;
    /// <summary>Gets whether the session service has released.</summary>
    public bool disposed { get; private set; }
    /// <inheritdoc/>
    public bool CanLower(NativeTypeView type, LoweringContext context) => type.type.category == NativeTypeCategory.Record && type.declaration?.qualifiedName == "Sample::Quantity";
    /// <inheritdoc/>
    public NativeTypeLoweringPlan Lower(NativeTypeView type, LoweringContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BridgeType scalar = new(new StableId("fixture-quantity-int"), BridgeTypeKind.Scalar, "int32_t") { nativeType = type.id };
        return new(scalar.id, [scalar], BridgeConversionKind.ScalarCast, BridgeConversionKind.ScalarCast);
    }
    /// <inheritdoc/>
    public void Dispose() => disposed = true;
}
