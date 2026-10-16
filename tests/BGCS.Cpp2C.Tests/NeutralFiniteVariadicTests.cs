using System;
using System.Collections.Immutable;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.Contracts.Requests;
using BGCS.Cpp2C.Planning;
using Xunit;

namespace BGCS.Cpp2C.Tests;

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
            [source.GetDeclaration(new("parameter")), original with { isVariadic = false }], []) { language = source.language };
        Assert.Throws<NotSupportedException>(() => new NativeVariadicAdapterPlanner().Plan(ordinary, [Request()]));
        NativeModule ambiguous = new(source.targetId, source.triple, source.pointerSize, source.types,
            [source.GetDeclaration(new("parameter")), original, original with { id = new("overload") }], []) { language = source.language };
        Assert.Throws<ArgumentException>(() => new NativeVariadicAdapterPlanner().Plan(ambiguous, [Request()]));
    }

    [Fact]
    public void ReparseMustKeepOriginalResultAndPrefixBeforeAnyFixedWrapperCanBeExported()
    {
        NativeVariadicAdapterPlanner planner = new();
        NativeModule source = Source(SourceLanguage.C);
        NativeVariadicAdapterPlan plan = planner.Plan(source, [Request() with { parameters = [] }]);
        NativeDeclaration wrapperParameter = source.GetDeclaration(new("parameter")) with { id = new("wrapper-parameter"), parent = new("wrapper") };
        NativeDeclaration wrapper = source.GetDeclaration(new("function")) with
        { id = new("wrapper"), name = "LogValues", qualifiedName = "LogValues", children = [wrapperParameter.id], isVariadic = false, isDefinition = true, isStatic = true };
        NativeModule expanded = Expanded(source, wrapperParameter, wrapper);
        Assert.Equal(new[] { wrapper.id }, planner.VerifyReparse(expanded, plan));
        NativeModule changed = Expanded(source, wrapperParameter with { type = new("float") }, wrapper);
        Assert.Throws<InvalidOperationException>(() => planner.VerifyReparse(changed, plan));
    }

    [Fact]
    public void ARequestedTypedefThatResolvesToAnObjectIsRejectedAfterTheRealNativeTypeBoundary()
    {
        NativeVariadicAdapterPlanner planner = new();
        NativeModule source = Source(SourceLanguage.C);
        NativeVariadicAdapterPlan plan = planner.Plan(source, [Request() with { parameters = [new("objectValue", "ObjectType")] }]);
        NativeDeclaration prefix = source.GetDeclaration(new("parameter")) with { id = new("prefix"), parent = new("wrapper") };
        NativeDeclaration extra = new(new("extra"), "Parameter", "objectValue", "LogValues::objectValue", new("object"), new("fixture.h", 5, 1));
        NativeDeclaration wrapper = source.GetDeclaration(new("function")) with
        { id = new("wrapper"), name = "LogValues", qualifiedName = "LogValues", children = [prefix.id, extra.id], isVariadic = false, isDefinition = true, isStatic = true };
        NativeModule expanded = new(source.targetId, source.triple, source.pointerSize,
            source.types.Add(new(new("object"), NativeTypeCategory.Record, "ObjectType", 4, 4)), source.declarations.AddRange(ImmutableArray.Create(prefix, extra, wrapper)), []) { language = source.language };
        Assert.Throws<NotSupportedException>(() => planner.VerifyReparse(expanded, plan));
    }

    private static VariadicVariantRequest Request() => new()
    { function = "Log", name = "LogValues", parameters = [new("narrow", "signed char"), new("real", "float")] };

    private static NativeModule Expanded(NativeModule source, NativeDeclaration parameter, NativeDeclaration wrapper) =>
        new(source.targetId, source.triple, source.pointerSize, source.types, source.declarations.AddRange(ImmutableArray.Create(parameter, wrapper)), []) { language = source.language };

    private static NativeModule Source(SourceLanguage language)
    {
        NativeType integer = new(new("int"), NativeTypeCategory.Primitive, "int", 4, 4) { primitiveKind = "Int", isSigned = true };
        NativeType real = new(new("float"), NativeTypeCategory.Primitive, "float", 4, 4) { primitiveKind = "Float" };
        NativeDeclaration parameter = new(new("parameter"), "Parameter", "marker", "Log::marker", integer.id, new("fixture.h", 1, 9)) { parent = new("function") };
        NativeDeclaration function = new(new("function"), "Function", "Log", "Log", null, new("fixture.h", 1, 1))
        { returnType = integer.id, children = [parameter.id], isVariadic = true, callingConvention = "C", nativeLinkage = NativeLinkageKind.External };
        return new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8, [integer, real], [function, parameter], []) { language = language };
    }
}
