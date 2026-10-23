using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using BGCS.Core.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.NativeBridge.Planning;
using Xunit;

namespace BGCS.NativeBridge.Tests;

public sealed class NeutralFiniteVariadicTests
{
    [Theory]
    [InlineData(SourceLanguage.C)]
    [InlineData(SourceLanguage.Cpp)]
    public void NativeWrapperKeepsTheFixedPrefixAndLetsTheNativeCallApplyEllipsisPromotions(SourceLanguage language)
    {
        NativeVariadicAdapterPlan plan = new NativeVariadicAdapterPlanner().Plan(Source(language), [Request()]);
        Assert.Contains("int marker, signed char narrow, float real", plan.source.text);
        Assert.DoesNotContain("LogValues(int marker, ...)", plan.source.text);
        Assert.Contains("typedef int (*bgcs_variadic_signature)(int, ...)", plan.source.text);
        Assert.Contains(language == SourceLanguage.C ? "bgcs_variadic_target(marker, narrow, real)"
            : "bgcs_variadic_target(static_cast<decltype(marker)&&>(marker), narrow, real)", plan.source.text);
        if (language == SourceLanguage.Cpp) Assert.Contains("static_cast<bgcs_variadic_signature>(&::Log)", plan.source.text);
        Assert.Equal(new StableId("function"), Assert.Single(plan.source.declarationDependencies));
        NativeVariadicAdapter adapter = Assert.Single(plan.adapters);
        Assert.Equal(1, adapter.fixedParameterCount);
        Assert.Equal("Log(int, ...)", adapter.sourceSignature);
        Assert.Equal(language == SourceLanguage.C ? "LogValues" : "BGCSGeneratedVariadics::LogValues", adapter.wrapper);
        Assert.Equal(EvidenceLevel.UserAsserted, plan.source.evidence.level);
    }

    [Theory]
    [InlineData("int; void Inject(void)")]
    [InlineData("int\n#error injected")]
    [InlineData("int (*)(int)")]
    [InlineData("int[4]")]
    public void NativeTypeComponentsCannotInjectDeclarationsOrSilentlyAdjustArrayAndFunctionArguments(string nativeType)
    {
        VariadicVariantRequest request = Request() with { parameters = [new("value", nativeType)] };
        Assert.Throws<ArgumentException>(() => new NativeVariadicAdapterPlanner().ValidateRequests([request]));
    }

    [Fact]
    public void ConcreteWrappersCannotReplaceOrdinaryFunctionsOrSelectAnAmbiguousOverload()
    {
        NativeModule source = Source(SourceLanguage.C);
        NativeDeclaration original = source.GetDeclaration(new("function"));
        NativeModule ordinary = new(source.targetId, source.triple, source.pointerSize, source.types,
            [source.GetDeclaration(new("parameter")), original with { isVariadic = false }], [])
        { language = source.language };
        Assert.Throws<NotSupportedException>(() => new NativeVariadicAdapterPlanner().Plan(ordinary, [Request()]));
        NativeModule ambiguous = new(source.targetId, source.triple, source.pointerSize, source.types,
            [source.GetDeclaration(new("parameter")), original, original with { id = new("overload") }], [])
        { language = source.language };
        Assert.Throws<ArgumentException>(() => new NativeVariadicAdapterPlanner().Plan(ambiguous, [Request()]));
    }

    [Fact]
    public void MissingActualTypeQueriesAreRejectedRatherThanInventingNativeTypes()
    {
        NativeModule source = Source(SourceLanguage.C);
        NativeModule missing = new(source.targetId, source.triple, source.pointerSize, source.types,
            source.declarations.Where(declaration => declaration.kind != "Typedef").ToImmutableArray(), [])
        { language = source.language };
        Assert.Throws<InvalidOperationException>(() => new NativeVariadicAdapterPlanner().Plan(missing, [Request()]));
    }

    [Fact]
    public void ARequestedTypedefThatResolvesToAnObjectIsRejectedAtTheOriginalFactBoundary()
    {
        NativeModule source = Source(SourceLanguage.C);
        NativeType narrow = source.GetType(new("narrow"));
        NativeModule objects = new(source.targetId, source.triple, source.pointerSize,
            source.types.Replace(narrow, narrow with { category = NativeTypeCategory.Record }), source.declarations, [])
        { language = source.language };
        Assert.Throws<NotSupportedException>(() => new NativeVariadicAdapterPlanner().Plan(objects, [Request()]));
    }

    private static VariadicVariantRequest Request() => new()
    { function = "Log", name = "LogValues", parameters = [new("narrow", "signed char"), new("real", "float")] };

    private static NativeModule Source(SourceLanguage language)
    {
        NativeType integer = new(new("int"), NativeTypeCategory.Primitive, "int", 4, 4) { primitiveKind = "Int", isSigned = true };
        NativeType real = new(new("float"), NativeTypeCategory.Primitive, "float", 4, 4) { primitiveKind = "Float" };
        NativeDeclaration parameter = new(new("parameter"), "Parameter", "marker", "Log::marker", integer.id, new("fixture.h", 1, 9)) { parent = new("function") };
        NativeDeclaration function = new(new("function"), "Function", "Log", "Log", null, new("fixture.h", 1, 1))
        { returnType = integer.id, children = [parameter.id], isVariadic = true, callingConvention = "C", nativeLinkage = NativeLinkageKind.External };
        NativeType narrow = new(new("narrow"), NativeTypeCategory.Primitive, "signed char", 1, 1) { primitiveKind = "SChar", isSigned = true };
        string probeSource = new NativeVariadicAdapterPlanner().TypeProbePostamble([Request()], language);
        string[] names = Regex.Matches(probeSource, @"BGCSFiniteType_[A-Za-z0-9_]+").Select(match => match.Value).ToArray();
        NativeDeclaration[] probes = names.Select((
            name,
            index
        ) => new NativeDeclaration(new("probe-declaration:" + index), "Typedef", name,
            (language == SourceLanguage.Cpp ? "BGCSGeneratedVariadicTypes::" : "") + name, new("probe-type:" + index), new("fixture.h", index + 2, 1))).ToArray();
        NativeType[] probeTypes = probes.Select((
            probe,
            index
        ) => new NativeType(probe.type!.Value, NativeTypeCategory.Alias, probe.qualifiedName,
            index == 0 ? 1 : 4, index == 0 ? 1 : 4)
        { declaration = probe.id, elementType = index == 0 ? narrow.id : real.id }).ToArray();
        return new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8, ImmutableArray.Create(integer, real, narrow).AddRange(probeTypes),
            ImmutableArray.Create(function, parameter).AddRange(probes), [])
        { language = language };
    }
}
