using BGCS.Frontend.Clang;
using BGCS.Frontend.Clang.Workers;
using BGCS.Frontend.Clang.Parsing.Arguments;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Analysis.Interop;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using Xunit;

namespace BGCS.Worker.Tests;

public sealed class CDeclarationAnalyzerTests : WorkerTestHost
{
    [Fact]
    public void CAbiAnalysis_PreservesNativeSignednessOffsetsAndSignatures()
    {
        NativeModule source = SourceModule();
        var interop = new CDeclarationAnalyzer().Analyze(source, "example");
        Assert.Empty(interop.diagnostics);
        Assert.Equal(source.targetId, interop.targetId);
        Assert.Equal("consume", Assert.Single(interop.functions).exportName);
        var record = interop.GetType(new("record-type"));
        Assert.Equal(4, Assert.Single(record.fields).offset);
        Assert.True(interop.GetType(new("int-type")).isSigned);
        Assert.Equal("C", Assert.Single(interop.functions).callingConvention);
    }

    [Fact]
    public void UnknownRecordByValue_FailsWhileItsPointerSignatureRemainsAvailable()
    {
        NativeModule source = SourceModule();
        var types = source.types.Select(type => type.id.value == "record-type" ? type with { size = null, alignment = null } : type).ToImmutableArray();
        NativeModule opaque = new(source.targetId, source.triple, source.pointerSize, types, source.declarations, []) { language = SourceLanguage.C };
        Assert.Empty(new CDeclarationAnalyzer().Analyze(opaque, "example").diagnostics);
        var declarations = source.declarations.Select(declaration => declaration.id.value == "parameter" ? declaration with { type = new StableId("record-type") } : declaration).ToImmutableArray();
        NativeModule byValue = new(source.targetId, source.triple, source.pointerSize, types, declarations, []) { language = SourceLanguage.C };
        Assert.Contains(new CDeclarationAnalyzer().Analyze(byValue, "example").diagnostics, diagnostic => diagnostic.message.Contains("unknown"));
    }

    [Fact]
    public void CppFreeFunction_IsNotAssumedToHaveAnUnmangledCExport()
    {
        NativeModule c = SourceModule();
        NativeModule cpp = new(c.targetId, c.triple, c.pointerSize, c.types, c.declarations, []) { language = SourceLanguage.Cpp };
        var result = new CDeclarationAnalyzer().Analyze(cpp, "example");
        Assert.Empty(result.functions);
        Assert.Contains(result.diagnostics, diagnostic => diagnostic.message.Contains("lowered"));
    }

    private static NativeModule SourceModule()
    {
        NativeType integer = new(new("int-type"), NativeTypeCategory.Primitive, "int", 4, 4) { primitiveKind = "Int", isSigned = true };
        NativeType record = new(new("record-type"), NativeTypeCategory.Record, "Item", 8, 4) { declaration = new StableId("record") };
        NativeType pointer = new(new("pointer-type"), NativeTypeCategory.Pointer, "Item*", 8, 8) { elementType = record.id };
        NativeDeclaration item = new(new("record"), "Class", "Item", "Item", record.id, default) { role = "Struct", children = [new StableId("field")] };
        NativeDeclaration field = new(new("field"), "Field", "value", "Item::value", integer.id, default) { parent = item.id, offset = 4, bitOffset = 32 };
        NativeDeclaration function = new(new("function"), "Function", "consume", "consume", null, default)
        {
            returnType = integer.id, callingConvention = "C", children = [new StableId("parameter")]
        };
        NativeDeclaration parameter = new(new("parameter"), "Parameter", "value", "consume::value", pointer.id, default) { parent = function.id };
        return new("linux-x64-gnu", "x86_64-unknown-linux-gnu", 8, [integer, record, pointer], [item, field, function, parameter], []) { language = SourceLanguage.C };
    }
}
