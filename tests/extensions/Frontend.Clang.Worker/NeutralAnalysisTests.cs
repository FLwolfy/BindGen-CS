using BGCS.Frontend.Clang;
using BGCS.Frontend.Clang.Workers;
using BGCS.Frontend.Clang.Parsing.Arguments;
using System.Collections.Immutable;
using BGCS.Analysis.Interop;
using BGCS.Analysis.Layout;
using BGCS.Core.Common;
using BGCS.Core.Native;
using Xunit;

namespace BGCS.Worker.Tests;

public sealed class NeutralAnalysisTests : WorkerTestHost
{
    [Fact]
    public void MissingTypeReference_IsRejectedWithoutAParser()
    {
        NativeType type = new(new("pointer"), NativeTypeCategory.Pointer, "Unknown*", 8, 8)
        {
            elementType = new StableId("missing")
        };
        NativeModule module = Module(type);
        Assert.Single(new NativeModuleVerifier().Validate(module));
    }

    [Fact]
    public void PointerWidthMismatch_IsRejectedFromTargetFacts()
    {
        NativeType pointer = new(new("pointer"), NativeTypeCategory.Pointer, "void*", 4, 4);
        Assert.Single(new NativeModuleVerifier().Validate(Module(pointer)));
    }

    [Fact]
    public void NativeStorageAndByValueCompatibility_AreSeparateDecisions()
    {
        NativeType record = new(new("record"), NativeTypeCategory.Record, "Aligned", 96, 32);
        NativeModule module = Module(record);
        Assert.Empty(new NativeModuleVerifier().Validate(module));
        Assert.NotNull(new ByValueCompatibilityAnalyzer().GetNativeRestriction(new(module, record.id)));
    }

    [Fact]
    public void UnknownAlignment_RemainsUnknown()
    {
        NativeType record = new(new("record"), NativeTypeCategory.Record, "Opaque", null, null);
        NativeModule module = Module(record);
        Assert.Empty(new NativeModuleVerifier().Validate(module));
        Assert.Null(module.GetType(record.id).alignment);
    }

    private static NativeModule Module(NativeType type) => new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8,
        ImmutableArray.Create(type), [], []);

    [Fact]
    public void NestedLongDoubleStorage_IsNotProofOfCallingCompatibility()
    {
        NativeType scalar = new(new("long-double"), NativeTypeCategory.Primitive, "long double", 16, 16) { primitiveKind = "LongDouble" };
        NativeType record = new(new("record"), NativeTypeCategory.Record, "Value", 16, 16) { declaration = new StableId("value-declaration") };
        NativeDeclaration declaration = new(new("value-declaration"), "Class", "Value", "Value", record.id, default)
        {
            children = [new StableId("field")]
        };
        NativeDeclaration field = new(new("field"), "Field", "value", "Value::value", scalar.id, default)
        {
            parent = declaration.id,
            offset = 0
        };
        NativeModule module = new("linux-x64-gnu", "x86_64-unknown-linux-gnu", 8, [scalar, record], [declaration, field], []);
        Assert.Empty(new NativeModuleVerifier().Validate(module));
        Assert.Contains("long double", new ByValueCompatibilityAnalyzer().GetNativeRestriction(new(module, record.id))!);
    }

    [Fact]
    public void FieldStartWithinRecord_DoesNotProveTheFieldFits()
    {
        NativeType scalar = new(new("int64"), NativeTypeCategory.Primitive, "long long", 8, 8);
        NativeType record = new(new("record"), NativeTypeCategory.Record, "Broken", 8, 4) { declaration = new StableId("record-declaration") };
        NativeDeclaration declaration = new(new("record-declaration"), "Class", "Broken", "Broken", record.id, default);
        NativeDeclaration field = new(new("field"), "Field", "value", "Broken::value", scalar.id, default)
        {
            parent = declaration.id,
            offset = 4
        };
        NativeModule module = new("linux-x64-gnu", "x86_64-unknown-linux-gnu", 8, [scalar, record], [declaration with { children = [field.id] }, field], []);
        Assert.Single(new NativeModuleVerifier().Validate(module));
    }
}
