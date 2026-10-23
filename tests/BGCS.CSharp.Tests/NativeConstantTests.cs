using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using BGCS.CSharp.Validation;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class NativeConstantTests
{
    [Theory]
    [InlineData(8, false, "18446744073709551615", "ulong", "18446744073709551615UL")]
    [InlineData(8, true, "-9223372036854775808", "long", "-9223372036854775808L")]
    [InlineData(4, false, "4294967295", "uint", "4294967295U")]
    [InlineData(1, true, "-128", "sbyte", "-128")]
    public void NativeWidthAndFullIntegralRangeArePreserved(int width, bool signed, string value, string type, string expression)
    {
        CSharpConstantPlan planned = Assert.Single(new CSharpConstantPlanner().Plan(Model(new(NativeScalarConstantKind.Integer, value, width, signed, Evidence))));
        Assert.Equal(type, planned.type);
        Assert.Equal(expression, planned.expression);
        Assert.True(planned.isCompileTimeConstant);
    }

    [Theory]
    [InlineData(NativeScalarConstantKind.Integer, "256", 1, false)]
    [InlineData(NativeScalarConstantKind.Integer, "18446744073709551616", 8, false)]
    [InlineData(NativeScalarConstantKind.Integer, "1 << 63", 8, false)]
    [InlineData(NativeScalarConstantKind.Integer, "01", 1, false)]
    [InlineData(NativeScalarConstantKind.Binary32, "80000000", 8, false)]
    [InlineData(NativeScalarConstantKind.Binary64, "800000000000000", 8, false)]
    public void UnsupportedOrOutOfRangeRepresentationsAreRejected(NativeScalarConstantKind kind, string value, int width, bool signed)
        => Assert.Throws<ArgumentException>(() => new CSharpConstantPlanner().Plan(Model(new(kind, value, width, kind == NativeScalarConstantKind.Integer ? signed : null, Evidence))));

    [Fact]
    public void FloatBitsPreserveNegativeZeroAndNanPayloadWithoutDecimalReinterpretation()
    {
        CSharpConstantPlan single = Assert.Single(new CSharpConstantPlanner().Plan(Model(new(NativeScalarConstantKind.Binary32, "80000000", 4, null, Evidence))));
        CSharpConstantPlan nan = Assert.Single(new CSharpConstantPlanner().Plan(Model(new(NativeScalarConstantKind.Binary64, "7FF8000000000123", 8, null, Evidence))));
        Assert.Contains("UInt32BitsToSingle(0x80000000U)", single.expression);
        Assert.Contains("UInt64BitsToDouble(0x7FF8000000000123UL)", nan.expression);
        Assert.False(single.isCompileTimeConstant);
        Assert.False(nan.isCompileTimeConstant);
    }

    [Theory]
    [InlineData("Constants")]
    [InlineData("GetType")]
    [InlineData("Equals")]
    public void LifecycleAndContainerNameCollisionsAreRejectedBeforeWritingSource(string name)
    {
        ManagedApiModel model = Model(new(NativeScalarConstantKind.Integer, "42", 4, true, Evidence));
        Assert.Throws<ArgumentException>(() => new CSharpConstantPlanner().Plan(model with { constants = [new(new("maximum"), name)] }));
        Assert.Throws<ArgumentException>(() => new CSharpConstantPlanner().Plan(model with { name = "Constants" }));
    }

    [Fact]
    public void UnknownEvidenceDoesNotBecomeACompilerEstablishedValue()
        => Assert.Throws<ArgumentException>(() => new CSharpConstantPlanner().Plan(Model(new(NativeScalarConstantKind.Integer, "42", 4, true, new(EvidenceLevel.Unknown, "missing-fact")))));

    [Theory]
    [InlineData("value")]
    [InlineData("type")]
    [InlineData("visibility")]
    [InlineData("duplicate")]
    public async Task ArtifactProcessingCannotSilentlyChangeThePlannedNativeValue(string mutation)
    {
        ManagedApiModel model = Model(new(NativeScalarConstantKind.Integer, "18446744073709551615", 8, false, Evidence));
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native", sink);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        string path = "Api/ConstantApi.Constants.g.cs";
        string original = sink.sources[path];
        string modified = mutation switch
        {
            "value" => original.Replace("18446744073709551615UL", "0UL", StringComparison.Ordinal),
            "type" => original.Replace("const ulong", "const uint", StringComparison.Ordinal),
            "visibility" => original.Replace("public const", "private const", StringComparison.Ordinal),
            _ => original.Replace("public const ulong Maximum = 18446744073709551615UL;", "public const ulong Maximum = 18446744073709551615UL; public const ulong Maximum = 18446744073709551615UL;", StringComparison.Ordinal)
        };
        Assert.NotEqual(original, modified);
        Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan,
            sink.sources.Select(source => (source.Key, source.Key == path ? modified : source.Value)), "native"));
    }

    private static Evidence Evidence => new(EvidenceLevel.Declared, "test.native.compiler-constant");

    private static ManagedApiModel Model(NativeScalarConstant value)
    {
        InteropModule interop = new("constants", "windows-x64-msvc", 8, [], [], [])
        { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian, constants = [new(new("maximum"), "MAXIMUM", "MAXIMUM", value, null)] };
        return new DefaultManagedProjection().Project(interop, new("Generated.Constants", "ConstantApi")
        { names = ImmutableDictionary<string, string>.Empty.Add("MAXIMUM", "Maximum") }, CancellationToken.None);
    }
}
