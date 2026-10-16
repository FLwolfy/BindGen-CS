using System;
using System.Linq;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.Cpp2C.Planning;
using Xunit;

namespace BGCS.Cpp2C.Tests;

public sealed class NeutralVirtualCallbackTests
{
    [Fact]
    public void DetachedVirtualInterface_ProducesExactStatusResultSlotsAndFiniteSource()
    {
        var plan = new VirtualCallbackPlanner().Plan(Module(), new("calculator", "Sample::Calculator"));
        Assert.Equal("#include <cstdint>\n#include <memory>\n#include <stdexcept>\n#include <utility>\n", VirtualCallbackPlanner.preamble);
        Assert.DoesNotContain("#include", plan.source.text);
        var slot = Assert.Single(plan.slots);
        Assert.Equal("int32_t", slot.result!.cType);
        Assert.Equal("int32_t", Assert.Single(slot.parameters).cType);
        Assert.Contains("void* userData, void* slot0", plan.source.text);
        Assert.Contains("int32_t bgcs_status = m_slot0(m_userData, static_cast<int32_t>(bgcs_arg0), &bgcs_result)", plan.source.text);
        Assert.Contains("noexcept(false) override", plan.source.text);
        Assert.Contains("if (bgcs_status != 0) throw", plan.source.text);
        Assert.Contains("&) = delete", plan.source.text);
        Assert.Equal(EvidenceLevel.Declared, plan.source.evidence.level);
        Assert.StartsWith("BGCSGeneratedCallbacks::Create_", plan.factory);
        Assert.Equal("unknown", plan.releaseThreadDomain);
        Assert.Equal(EvidenceLevel.Unknown, plan.cleanupEvidence.level);
    }

    [Fact]
    public void ExplicitAnyThreadCleanup_RemainsAUserAssertionWithoutNativeLifetimeVerification()
    {
        var plan = new VirtualCallbackPlanner().Plan(Module(), new("calculator", "Sample::Calculator") { releaseThreadDomain = "any" });
        Assert.Equal("any", plan.releaseThreadDomain);
        Assert.Equal(EvidenceLevel.UserAsserted, plan.cleanupEvidence.level);
        Assert.Equal(EvidenceLevel.Declared, plan.source.evidence.level);
        Assert.Throws<ArgumentException>(() => new VirtualCallbackPlanner().Plan(Module(), new("invalid", "Sample::Calculator") { releaseThreadDomain = "affine" }));
    }

    [Fact]
    public void VolatileAndReferenceQualifiedOverrides_RemainDistinctAndExact()
    {
        NativeModule original = Module();
        NativeDeclaration plain = original.declarations[1];
        NativeDeclaration qualified = plain with { id = new("compute-qualified"), isVolatile = true, referenceQualifier = NativeReferenceQualifier.LValue, children = [new("qualified-value")] };
        NativeDeclaration parameter = original.declarations[2] with { id = new("qualified-value"), parent = qualified.id };
        NativeDeclaration owner = original.declarations[0] with { children = [plain.id, qualified.id] };
        NativeModule module = new(original.targetId, original.triple, original.pointerSize, original.types, [owner, plain, original.declarations[2], qualified, parameter], [])
        { language = original.language, byteOrder = original.byteOrder };

        var plan = new VirtualCallbackPlanner().Plan(module, new("qualified", "Sample::Calculator"));

        Assert.Equal(2, plan.slots.Length);
        Assert.Contains(" const volatile & noexcept(false) override", plan.source.text);
        Assert.Contains(" const noexcept(false) override", plan.source.text);
        Assert.Equal(2, plan.slots.Select(slot => slot.nativeMethod).Distinct().Count());
    }

    [Theory]
    [InlineData("unproved-return")]
    [InlineData("no-prototype")]
    [InlineData("not-abstract")]
    [InlineData("nothrow")]
    [InlineData("unknown-exception")]
    [InlineData("unknown-reference")]
    public void UnsupportedVirtualShapes_FailWithAnExplicitNativeRequirement(string fault)
    {
        NativeModule valid = Module();
        NativeDeclaration owner = valid.declarations[0], method = valid.declarations[1];
        NativeType integer = valid.types[0];
        NativeModule invalid = fault switch
        {
            "unproved-return" => new(valid.targetId, valid.triple, valid.pointerSize, [integer with { primitiveKind = "LongDouble", size = 16 }, valid.types[1]], valid.declarations, []),
            "no-prototype" => new(valid.targetId, valid.triple, valid.pointerSize, valid.types, [owner, method with { hasPrototype = false }, valid.declarations[2]], []),
            "not-abstract" => new(valid.targetId, valid.triple, valid.pointerSize, valid.types, [owner with { isAbstract = false }, method, valid.declarations[2]], []),
            "nothrow" => new(valid.targetId, valid.triple, valid.pointerSize, valid.types, [owner, method with { isNoThrow = true }, valid.declarations[2]], []),
            "unknown-exception" => new(valid.targetId, valid.triple, valid.pointerSize, valid.types, [owner, method with { isNoThrow = null }, valid.declarations[2]], []),
            "unknown-reference" => new(valid.targetId, valid.triple, valid.pointerSize, valid.types, [owner, method with { referenceQualifier = NativeReferenceQualifier.Unknown }, valid.declarations[2]], []),
            _ => throw new ArgumentException(nameof(fault))
        };
        Assert.Throws<NotSupportedException>(() => new VirtualCallbackPlanner().Plan(invalid, new("calculator", "Sample::Calculator")));
    }

    private static NativeModule Module()
    {
        SourceLocation location = new("fixture.hpp", 1, 1);
        NativeType integer = new(new("integer"), NativeTypeCategory.Primitive, "int", 4, 4) { primitiveKind = "Int", isSigned = true };
        NativeType record = new(new("interface"), NativeTypeCategory.Record, "Sample::Calculator", 8, 8) { declaration = new("interface-declaration") };
        NativeDeclaration owner = new(new("interface-declaration"), "Class", "Calculator", "Sample::Calculator", record.id, location)
        { isAbstract = true, isCompleteDefinition = true, children = [new("compute")] };
        NativeDeclaration method = new(new("compute"), "Function", "Compute", "Sample::Calculator::Compute", null, location)
        { parent = owner.id, isAbstract = true, isVirtual = true, isConst = true, isNoThrow = false, returnType = integer.id, children = [new("value")] };
        NativeDeclaration value = new(new("value"), "Parameter", "value", "value", integer.id, location) { parent = method.id };
        return new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8, [integer, record], [owner, method, value], []) { language = SourceLanguage.Cpp };
    }
}
