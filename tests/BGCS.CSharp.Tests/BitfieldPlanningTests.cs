using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Native;
using BGCS.Contracts.Requests;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using BGCS.CSharp.Validation;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class BitfieldPlanningTests
{
    [Fact]
    public async Task NativeBooleanBitfieldUsesBooleanAccessWithoutInventingIntegerSignedness()
    {
        InteropType boolean = new(new("boolean"), "_Bool", NativeTypeCategory.Primitive, 1, 1) { primitiveKind = "Bool" };
        InteropType bits = new(new("bits"), "Bits", NativeTypeCategory.Record, 1, 1)
        { declarationName = "Bits", fields = [new(new("visible"), "visible", boolean.id, 0, 3, 1)] };
        InteropModule module = new("bits", "windows-x64-msvc", 8, [boolean, bits], [], []) { byteOrder = NativeByteOrder.LittleEndian };
        var model = new DefaultManagedProjection().Project(module, new("Generated.Bits", "BitsApi"), CancellationToken.None);
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("fixture", sink);
        CSharpBitfieldPlan field = Assert.Single(Assert.Single(new CSharpEmissionPlanner().Plan(model, request).storage).bitfields);
        Assert.Equal("bool", field.type);
        await new CSharpBackend().EmitAsync(model, request, CancellationToken.None);
        Assert.Contains(sink.sources.Values, source => source.Contains("return bits != 0;", StringComparison.Ordinal));
        Assert.Contains(sink.sources.Values, source => source.Contains("value ? 1UL : 0UL", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(NativeByteOrder.Unknown)]
    [InlineData(NativeByteOrder.BigEndian)]
    public async Task UnprovenBitOrder_RejectsBeforeWriting(NativeByteOrder order)
    {
        var model = new DefaultManagedProjection().Project(Module(order), new("Generated.Bits", "BitsApi"), CancellationToken.None);
        NeutralBackendTests.MemorySink sink = new();
        await Assert.ThrowsAsync<CSharpPlanningException>(async () => await new CSharpBackend().EmitAsync(model, new("unused", sink), CancellationToken.None));
        Assert.Empty(sink.sources);
    }

    [Fact]
    public async Task ProcessedBitfieldShape_IsRecheckedAgainstNativePositions()
    {
        var model = new DefaultManagedProjection().Project(Module(NativeByteOrder.LittleEndian), new("Generated.Bits", "BitsApi"), CancellationToken.None);
        NeutralBackendTests.MemorySink sink = new();
        CSharpEmissionPlan plan = new CSharpEmissionPlanner().Plan(model, new("unused", sink));
        Assert.Equal(3, Assert.Single(plan.storage).bitfields[0].bitOffset);
        await new CSharpBackend().EmitAsync(model, new("unused", sink), CancellationToken.None);
        EmittedInteropContractReader reader = new();
        reader.Verify(plan, sink.sources.Select(source => (source.Key, source.Value)), "unused");
        Assert.Throws<InvalidOperationException>(() => reader.Verify(plan,
            sink.sources.Select(source => (source.Key, source.Value.Replace("3L + index", "4L + index", StringComparison.Ordinal))), "unused"));
        Assert.Throws<InvalidOperationException>(() => reader.Verify(plan,
            sink.sources.Select(source => (source.Key, source.Value.Replace("m_bitStorage[4]", "m_bitStorage[3]", StringComparison.Ordinal))), "unused"));
    }

    private static InteropModule Module(NativeByteOrder order)
    {
        InteropType integer = new(new("int"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropType bits = new(new("bits"), "Bits", NativeTypeCategory.Record, 4, 4)
        { declarationName = "Bits", fields = [new(new("gap"), "", integer.id, 0, 0, 3), new(new("value"), "value", integer.id, 0, 3, 6)] };
        return new("bits", "windows-x64-msvc", 8, [integer, bits], [], []) { byteOrder = order };
    }
}
