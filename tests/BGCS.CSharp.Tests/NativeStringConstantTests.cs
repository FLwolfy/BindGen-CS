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
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class NativeStringConstantTests
{
    [Fact]
    public void UnicodeLiteralKindsPreserveSupplementaryAndEmbeddedZeroUnitsWithoutTextReinterpretation()
    {
        foreach (NativeStringConstant value in new[]
        {
            Value(NativeStringLiteralKind.Utf8, 1, [0xF0, 0x9F, 0x98, 0x80, 0, 90, 0]),
            Value(NativeStringLiteralKind.Utf16, 2, [0xD83D, 0xDE00, 0, 90, 0]),
            Value(NativeStringLiteralKind.Utf32, 4, [0x1F600, 0, 90, 0])
        })
        {
            CSharpStringConstantPlan plan = Assert.Single(new CSharpStringConstantPlanner().Plan(Model(value)));
            Assert.True(plan.isUnicodeString);
            Assert.Equal("string", plan.type);
            Assert.Equal("😀\0Z", Assert.IsType<LiteralExpressionSyntax>(SyntaxFactory.ParseExpression(plan.expression)).Token.ValueText);
            Assert.Equal(value.codeUnits.Length, plan.nativeUnitCount);
            Assert.Empty(new CSharpConstantPlanner().Plan(Model(value)));
        }
    }

    [Theory]
    [InlineData(NativeStringLiteralKind.Ordinary, 1, 255U, "byte")]
    [InlineData(NativeStringLiteralKind.Utf8, 1, 255U, "byte")]
    [InlineData(NativeStringLiteralKind.Wide, 2, 55357U, "ushort")]
    [InlineData(NativeStringLiteralKind.Utf32, 4, 4294967295U, "uint")]
    public void OpaqueOrNonUnicodeUnitsRemainExactReadonlyDataIncludingTheFinalZero(
        NativeStringLiteralKind kind,
        int width,
        uint unit,
        string carrier
    ) {
        CSharpStringConstantPlan plan = Assert.Single(new CSharpStringConstantPlanner().Plan(Model(Value(kind, width, [unit, 0]))));
        Assert.False(plan.isUnicodeString);
        Assert.Equal("global::System.ReadOnlySpan<" + carrier + ">", plan.type);
        Assert.Contains(unit.ToString("X", System.Globalization.CultureInfo.InvariantCulture), plan.expression);
        Assert.EndsWith(width == 4 ? ", 0x0U]" : ", 0x0]", plan.expression);
    }

    [Theory]
    [InlineData("unit")]
    [InlineData("width")]
    [InlineData("visibility")]
    [InlineData("mutable")]
    public async Task ArtifactProcessorsCannotChangeLiteralDataOrItsReadonlyCarrier(string mutation)
    {
        ManagedApiModel model = Model(Value(NativeStringLiteralKind.Ordinary, 1, [255, 0]));
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native", sink);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        string path = "Api/LiteralApi.Constants.g.cs", original = sink.sources[path];
        string changed = mutation switch
        {
            "unit" => original.Replace("0xFF", "0xFE", StringComparison.Ordinal),
            "width" => original.Replace("ReadOnlySpan<byte>", "ReadOnlySpan<uint>", StringComparison.Ordinal),
            "visibility" => original.Replace("public static global::System.ReadOnlySpan", "private static global::System.ReadOnlySpan", StringComparison.Ordinal),
            _ => original.Replace("global::System.ReadOnlySpan<byte>", "byte[]", StringComparison.Ordinal)
        };
        Assert.NotEqual(original, changed);
        Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan,
            sink.sources.Select(source => (source.Key, source.Key == path ? changed : source.Value)), "native"));
    }

    [Fact]
    public void ScalarsAndStringsShareOneIdentityAndNameCollisionBoundary()
    {
        ManagedApiModel original = Model(Value(NativeStringLiteralKind.Utf8, 1, [65, 0]));
        InteropModule interop = new("literals", original.interop.targetId, original.interop.pointerSize, [], [], [])
        {
            stringConstants = original.interop.stringConstants,
            constants = [new(new("number"), "NUMBER", "NUMBER", new(NativeScalarConstantKind.Integer, "42", 4, true, Evidence), null)]
        };
        ManagedApiModel duplicate = original with { interop = interop, constants = [new(new("literal"), "Value"), new(new("number"), "Value")] };
        Assert.Throws<ArgumentException>(() => new CSharpConstantPlanner().Plan(duplicate));
        Assert.Throws<ArgumentException>(() => new CSharpStringConstantPlanner().Plan(duplicate));
        Assert.Throws<ArgumentException>(() => new CSharpConstantPlanner().Plan(original with { constants = [new(new("unknown"), "Unknown")] }));
    }

    private static Evidence Evidence => new(EvidenceLevel.Declared, "native:literal-facts");
    private static NativeStringConstant Value(
        NativeStringLiteralKind kind,
        int width,
        ImmutableArray<uint> units
    ) => new(kind, units, width, Evidence);
    private static ManagedApiModel Model(NativeStringConstant value)
    {
        InteropModule source = new("literals", "windows-x64-msvc", 8, [], [], [])
        { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian, stringConstants = [new(new("literal"), "LITERAL", "LITERAL", value, null)] };
        return new DefaultManagedProjection().Project(source, new("Generated.Literals", "LiteralApi")
        { names = ImmutableDictionary<string, string>.Empty.Add("LITERAL", "Value") }, CancellationToken.None);
    }
}
