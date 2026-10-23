using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using BGCS.CSharp.Validation;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class ManagedEnumTests
{
    [Theory]
    [InlineData(ManagedIntegralKind.UInt64, "18446744073709551615", "18446744073709551615UL")]
    [InlineData(ManagedIntegralKind.Int64, "-9223372036854775808", "-9223372036854775808L")]
    public void FullWidthConstantsArePreserved(
        ManagedIntegralKind kind,
        string value,
        string literal
    ) {
        CSharpManagedEnumPlan plan = Assert.Single(new ManagedEnumPlanner().Plan([new("Flags", kind, [new("Value", value)])]));
        Assert.Equal(literal, Assert.Single(plan.values).value);
    }

    [Theory]
    [InlineData(ManagedIntegralKind.UInt64, "18446744073709551616")]
    [InlineData(ManagedIntegralKind.UInt64, "-1")]
    [InlineData(ManagedIntegralKind.Int64, "9223372036854775808")]
    [InlineData(ManagedIntegralKind.Int8, "-129")]
    [InlineData(ManagedIntegralKind.UInt8, "256")]
    [InlineData(ManagedIntegralKind.UInt32, "1 << 31")]
    [InlineData(ManagedIntegralKind.Unknown, "1")]
    public void OverflowAndSourceExpressionsAreRejected(
        ManagedIntegralKind kind,
        string value
    )
        => Assert.Throws<ArgumentException>(() => new ManagedEnumPlanner().Plan([new("Flags", kind, [new("Value", value)])]));

    [Fact]
    public void AliasProjectionKeepsTheSeparateRawCarrierAndRejectsWidthSignednessOrAmbiguity()
    {
        InteropType primitive = new(new("u64"), "unsigned long long", NativeTypeCategory.Primitive, 8, 8) { primitiveKind = "UnsignedLongLong", isSigned = false };
        InteropType alias = new(new("flags"), "native_flags", NativeTypeCategory.Alias, 8, 8) { elementType = primitive.id };
        InteropFunction function = new(new("echo"), "echo", alias.id, [new(new("value"), "value", alias.id)], "C");
        InteropModule module = new("fixture", "windows-x64-msvc", 8, [primitive, alias], [function], []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        ManagedProjectionRequest request = new("Generated.Enums", "EnumApi")
        {
            enums = [new("Flags", ManagedIntegralKind.UInt64, [new("High", "9223372036854775808")])],
            enumMappings = ImmutableDictionary<string, string>.Empty.Add("native_flags", "Flags")
        };
        ManagedApiModel model = new DefaultManagedProjection().Project(module, request, CancellationToken.None);
        NeutralBackendTests.MemorySink sink = new();
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, new("native", sink));
        Assert.Equal("ulong", Assert.Single(plan.methods).returnType);
        Assert.Equal("ulong", Assert.Single(plan.methods).parameterTypes[0]);
        Assert.Equal("global::Generated.Enums.Flags", Assert.Single(plan.methods).returnProjection!.managedType);
        Assert.Same(module, model.interop);
        Assert.Throws<NotSupportedException>(() => new DefaultManagedProjection().Project(module,
            request with { enums = [new("Flags", ManagedIntegralKind.Int64, [])] }, CancellationToken.None));
        Assert.Throws<NotSupportedException>(() => new DefaultManagedProjection().Project(module,
            request with { enums = [new("Flags", ManagedIntegralKind.UInt32, [])] }, CancellationToken.None));
        InteropModule duplicate = new("fixture", "windows-x64-msvc", 8, [primitive, alias, alias with { id = new("other") }], [function], []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        Assert.Throws<ArgumentException>(() => new DefaultManagedProjection().Project(duplicate, request, CancellationToken.None));
    }

    [Theory]
    [InlineData("value")]
    [InlineData("underlying")]
    [InlineData("flags")]
    [InlineData("visibility")]
    [InlineData("duplicate")]
    public async Task ProcessedManagedEnumsMustStillMatchTheirDeclaredContract(string change)
    {
        ManagedApiModel model = NeutralBackendTests.Model() with
        { enums = [new("WideFlags", ManagedIntegralKind.UInt64, [new("High", "9223372036854775808")]) { isFlags = true }] };
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native", sink);
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        string enumSource = sink.sources["Types/WideFlags.g.cs"];
        string edited = change switch
        {
            "value" => enumSource.Replace("9223372036854775808UL", "1UL", StringComparison.Ordinal),
            "underlying" => enumSource.Replace("WideFlags : ulong", "WideFlags : uint", StringComparison.Ordinal),
            "flags" => enumSource.Replace("[global::System.Flags]", "", StringComparison.Ordinal),
            "visibility" => enumSource.Replace("public enum", "internal enum", StringComparison.Ordinal),
            "duplicate" => enumSource + "\npublic enum WideFlags : ulong {}\n",
            _ => throw new ArgumentException(nameof(change))
        };
        Assert.NotEqual(enumSource, edited);
        Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan,
            sink.sources.Select(pair => (pair.Key, pair.Key == "Types/WideFlags.g.cs" ? edited : pair.Value)), request.libraryName));
    }

    [Fact]
    public async Task UnusedInvalidDeclarationFailsBeforeAnyCandidateWrites()
    {
        ManagedApiModel model = NeutralBackendTests.Model() with { enums = [new("NativeApi", ManagedIntegralKind.UInt64, [])] };
        NeutralBackendTests.MemorySink sink = new();
        await Assert.ThrowsAsync<ArgumentException>(async () => await new CSharpTestBackend().EmitAsync(model, new("native", sink), CancellationToken.None));
        Assert.Empty(sink.sources);
    }
}
