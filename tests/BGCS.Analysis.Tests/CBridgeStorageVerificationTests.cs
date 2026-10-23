using System;
using System.Collections.Immutable;
using BGCS.Analysis.Bridge;
using BGCS.Core.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using Xunit;

namespace BGCS.Analysis.Tests;

public sealed class CBridgeStorageVerificationTests
{
    [Fact]
    public void ReparsedRecordMustPreserveFieldsEvenWhenTotalSizeAndAlignmentMatch()
    {
        NativeBridgePlan plan = Plan();
        NativeModule matching = Reparsed(plan.source, 8, true);
        var merged = new BridgeContractVerifier().VerifyAndMerge(plan, matching, "fixture");
        Assert.Single(merged.functions);
        NativeModule changed = Reparsed(plan.source, 4, true);
        Assert.Equal(plan.source.GetType(new("sample")).size, changed.GetType(new("sample")).size);
        Assert.Equal(plan.source.GetType(new("sample")).alignment, changed.GetType(new("sample")).alignment);
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => new BridgeContractVerifier().VerifyAndMerge(plan, changed, "fixture"));
        Assert.Contains("field", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReparsedBorrowedAggregateCannotLoseItsConstInputContract()
    {
        NativeBridgePlan plan = Plan();
        Assert.Throws<InvalidOperationException>(() => new BridgeContractVerifier().VerifyAndMerge(plan, Reparsed(plan.source, 8, false), "fixture"));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("case-changed")]
    [InlineData("no-prototype")]
    [InlineData("variadic")]
    [InlineData("static")]
    [InlineData("calling-convention")]
    public void EveryPhysicalExportMustHaveOneExactPublicPrototype(string mutation)
    {
        NativeBridgePlan plan = Plan();
        NativeModule matching = Reparsed(plan.source, 8, true);
        NativeDeclaration function = matching.GetDeclaration(new("physical-function"));
        ImmutableArray<NativeDeclaration> declarations = mutation switch
        {
            "missing" => matching.declarations.Remove(function),
            "duplicate" => matching.declarations.Add(function with { id = new("duplicate-physical-function"), children = [] }),
            "case-changed" => matching.declarations.Replace(function, function with { name = "FIXTURE_USE" }),
            "no-prototype" => matching.declarations.Replace(function, function with { hasPrototype = false }),
            "variadic" => matching.declarations.Replace(function, function with { isVariadic = true }),
            "static" => matching.declarations.Replace(function, function with { isStatic = true }),
            "calling-convention" => matching.declarations.Replace(function, function with { callingConvention = "StdCall" }),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        NativeModule changed = new(matching.targetId, matching.triple, matching.pointerSize, matching.types, declarations, [])
        { language = matching.language, byteOrder = matching.byteOrder };

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => new BridgeContractVerifier().VerifyAndMerge(plan, changed, "fixture"));
        Assert.Contains("missing or changed physical prototype", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReparsedExportCannotChangeItsReturnChannel()
    {
        NativeBridgePlan plan = Plan();
        NativeModule matching = Reparsed(plan.source, 8, true);
        NativeDeclaration function = matching.GetDeclaration(new("physical-function"));
        NativeModule changed = new(matching.targetId, matching.triple, matching.pointerSize, matching.types,
            matching.declarations.Replace(function, function with { returnType = new("int") }), [])
        { language = matching.language, byteOrder = matching.byteOrder };

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => new BridgeContractVerifier().VerifyAndMerge(plan, changed, "fixture"));
        Assert.Contains("unexpectedly returns a native value", failure.Message, StringComparison.Ordinal);
    }

    private static NativeBridgePlan Plan()
    {
        SourceLocation location = new("native.h", 1, 1);
        NativeType integer = new(new("int"), NativeTypeCategory.Primitive, "int", 4, 4) { primitiveKind = "Int", isSigned = true };
        NativeType none = new(new("void"), NativeTypeCategory.Primitive, "void", 0, null) { primitiveKind = "Void" };
        NativeType record = new(new("sample"), NativeTypeCategory.Record, "struct Sample", 16, 4) { declaration = new("sample-declaration") };
        NativeDeclaration owner = new(new("sample-declaration"), "Record", "Sample", "Sample", record.id, location)
        { role = "Struct", isCompleteDefinition = true, children = [new("first"), new("second")] };
        NativeDeclaration first = new(new("first"), "Field", "first", "Sample::first", integer.id, location) { parent = owner.id, offset = 0 };
        NativeDeclaration second = new(new("second"), "Field", "second", "Sample::second", integer.id, location) { parent = owner.id, offset = 8 };
        NativeDeclaration function = new(new("function"), "Function", "UseSample", "UseSample", null, location)
        { returnType = none.id, children = [new("parameter")], callingConvention = "C", linkage = "C", nativeLinkage = NativeLinkageKind.External };
        NativeDeclaration parameter = new(new("parameter"), "Parameter", "sample", "UseSample::sample", record.id, location) { parent = function.id };
        NativeModule source = new("fixture-target", "fixture-triple", 8, [integer, none, record], [owner, first, second, function, parameter], [])
        { language = SourceLanguage.C, byteOrder = NativeByteOrder.LittleEndian };
        BridgeType storage = new(new("sample-storage"), BridgeTypeKind.NativeStorage, "struct Sample") { nativeType = record.id };
        BridgeType pointer = new(new("sample-pointer"), BridgeTypeKind.Pointer, string.Empty) { elementType = storage.id, isConst = true };
        BridgeParameter input = new(new("bridge-parameter"), "arg0", pointer.id, record.id, BridgeConversionKind.Dereference);
        BridgeExport export = new(new("export"), "fixture_use", [input], new(BridgeOperationKind.FreeCall, function.id, null, null))
        { usesErrorChannel = false, resultInOutputParameter = false, nativeResultType = none.id };
        return new(source, "fixture", ["native.h"], [storage, pointer], [export]);
    }

    private static NativeModule Reparsed(
        NativeModule source,
        long secondOffset,
        bool readOnly
    ) {
        NativeDeclaration oldField = source.GetDeclaration(new("second"));
        ImmutableArray<NativeType> types = source.types;
        StableId element = new("sample");
        if (readOnly)
        {
            NativeType qualified = new(new("const-sample"), NativeTypeCategory.Qualified, "const struct Sample", 16, 4)
            { elementType = element, qualifier = "Const" };
            types = types.Add(qualified); element = qualified.id;
        }
        NativeType pointer = new(new("physical-pointer"), NativeTypeCategory.Pointer, readOnly ? "const struct Sample*" : "struct Sample*", 8, 8) { elementType = element };
        NativeDeclaration function = new(new("physical-function"), "Function", "fixture_use", "fixture_use", null, new("bridge.h", 1, 1))
        { returnType = new("void"), children = [new("physical-parameter")], callingConvention = "C", linkage = "C", nativeLinkage = NativeLinkageKind.External };
        NativeDeclaration parameter = new(new("physical-parameter"), "Parameter", "arg0", "fixture_use::arg0", pointer.id, function.source) { parent = function.id };
        return new(source.targetId, source.triple, source.pointerSize, types.Add(pointer),
            source.declarations.Replace(oldField, oldField with { offset = secondOffset }).Add(function).Add(parameter), [])
        { language = SourceLanguage.C, byteOrder = source.byteOrder };
    }
}
