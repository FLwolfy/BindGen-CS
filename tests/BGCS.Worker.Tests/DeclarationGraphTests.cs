using System;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Analysis.Declarations;
using BGCS.Contracts.Common;
using BGCS.Contracts.Native;
using Xunit;

namespace BGCS.Worker.Tests;

public sealed class DeclarationGraphTests
{
    [Fact]
    public void FunctionSelection_RetainsPointerRecordAndItsFieldShape()
    {
        NativeModule module = CreateModule();
        var closure = DependencyClosureBuilder.Build(DeclarationGraph.Create(module), [new StableId("read")]);
        Assert.Equal(4, closure.Length);
        Assert.Contains(new StableId("item"), closure);
        Assert.Contains(new StableId("field"), closure);
        Assert.Contains(new StableId("parameter"), closure);
        Assert.DoesNotContain(new StableId("unrelated-method"), closure);
    }

    [Fact]
    public void RecursivePointerTypeGraph_ProducesEveryDeclarationOnce()
    {
        NativeModule original = CreateModule();
        NativeDeclaration field = original.GetDeclaration(new("field")) with { type = new StableId("pointer-type") };
        NativeModule recursive = new(original.targetId, original.triple, original.pointerSize, original.types,
            original.declarations.Select(declaration => declaration.id == field.id ? field : declaration).ToImmutableArray(), []);
        var closure = DependencyClosureBuilder.Build(DeclarationGraph.Create(recursive), [new StableId("read")]);
        Assert.Equal(closure.Length, closure.Distinct().Count());
        Assert.Equal(4, closure.Length);
    }

    [Fact]
    public void InputFactOrdering_DoesNotChangeDependencyOrder()
    {
        NativeModule original = CreateModule();
        NativeModule reverse = new(original.targetId, original.triple, original.pointerSize,
            original.types.Reverse().ToImmutableArray(), original.declarations.Reverse().ToImmutableArray(), []);
        StableId[] roots = [new("read")];
        Assert.Equal(DependencyClosureBuilder.Build(DeclarationGraph.Create(original), roots).ToArray(),
            DependencyClosureBuilder.Build(DeclarationGraph.Create(reverse), roots).ToArray());
    }

    [Fact]
    public void MissingRoot_IsRejectedInsteadOfSilentlyOmitted()
    {
        Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() =>
            DependencyClosureBuilder.Build(DeclarationGraph.Create(CreateModule()), [new StableId("missing")]));
    }

    private static NativeModule CreateModule()
    {
        NativeType integer = new(new("int-type"), NativeTypeCategory.Primitive, "int", 4, 4);
        NativeType record = new(new("record-type"), NativeTypeCategory.Record, "Item", 4, 4) { declaration = new StableId("item") };
        NativeType pointer = new(new("pointer-type"), NativeTypeCategory.Pointer, "Item*", 8, 8) { elementType = record.id };
        NativeDeclaration item = new(new("item"), "Class", "Item", "Item", record.id, default) { children = [new StableId("field"), new StableId("unrelated-method")] };
        NativeDeclaration field = new(new("field"), "Field", "value", "Item::value", integer.id, default) { parent = item.id, offset = 0 };
        NativeDeclaration read = new(new("read"), "Function", "read", "read", null, default) { children = [new StableId("parameter")], returnType = integer.id };
        NativeDeclaration parameter = new(new("parameter"), "Parameter", "item", "read::item", pointer.id, default) { parent = read.id };
        NativeDeclaration method = new(new("unrelated-method"), "Function", "method", "Item::method", null, default) { parent = item.id, returnType = integer.id };
        return new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8, [integer, record, pointer], [item, field, read, parameter, method], []);
    }
}
