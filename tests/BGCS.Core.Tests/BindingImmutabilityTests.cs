using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;
using Xunit;

namespace BGCS.Core.Tests;

public sealed class BindingImmutabilityTests
{
    [Fact]
    public void NativeModuleRetainsFrozenNodesAndLookupsWhenTheBuilderChanges()
    {
        NativeType integer = new(new("integer"), NativeTypeCategory.Primitive, "int", 4, 4) { primitiveKind = "Int", isSigned = true };
        ImmutableArray<NativeType>.Builder builder = ImmutableArray.CreateBuilder<NativeType>();
        builder.Add(integer);
        NativeModule original = new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8, builder.ToImmutable(), [], []);
        NativeType replacement = integer with { spelling = "Changed", size = 8 };
        builder[0] = replacement;
        NativeModule next = new(original.targetId, original.triple, original.pointerSize, builder.ToImmutable(), [], []);
        builder.Clear();
        Assert.Same(integer, Assert.Single(original.types));
        Assert.Same(integer, original.GetType(integer.id));
        Assert.Same(replacement, next.GetType(integer.id));
        Assert.Equal("int", original.GetType(integer.id).spelling);
        Assert.Throws<NotSupportedException>(() => ((IList<NativeType>)original.types).Clear());
    }

    [Fact]
    public void NativeCallableAndDeclarationChildrenAreDetachedImmutableSnapshots()
    {
        StableId[] parameterIds = [new("integer")];
        NativeType callable = new(new("callback"), NativeTypeCategory.Function, "void(int)", null, null)
        { returnType = new("void"), parameterTypes = parameterIds.ToImmutableArray() };
        ImmutableArray<StableId>.Builder children = ImmutableArray.CreateBuilder<StableId>();
        children.Add(new("parameter"));
        NativeDeclaration declaration = new(new("function"), "Function", "consume", "consume", callable.id, new("native.h", 1, 1))
        { children = children.ToImmutable() };
        parameterIds[0] = new("changed");
        children.Clear();
        NativeDeclaration renamed = declaration with { name = "next", children = [] };
        Assert.Equal(new StableId("integer"), Assert.Single(callable.parameterTypes));
        Assert.Equal(new StableId("parameter"), Assert.Single(declaration.children));
        Assert.Empty(renamed.children);
        Assert.Equal("consume", declaration.name);
        Assert.Throws<NotSupportedException>(() => ((IList<StableId>)callable.parameterTypes)[0] = new("changed"));
    }

    [Fact]
    public void InteropFunctionAndRecordKeepParameterAndFieldSnapshotsIndependent()
    {
        List<InteropParameter> parameters = [new(new("argument"), "argument", new("integer"))];
        InteropFunction function = new(new("consume"), "consume", new("void"), parameters.ToImmutableArray(), "C");
        List<InteropField> fields = [new(new("field"), "field", new("integer"), 0, 0, null)];
        InteropType record = new(new("record"), "Record", NativeTypeCategory.Record, 4, 4) { fields = fields.ToImmutableArray() };
        parameters[0] = parameters[0] with { name = "changed" };
        fields.Clear();
        InteropFunction next = function with { parameters = [] };
        Assert.Equal("argument", Assert.Single(function.parameters).name);
        Assert.Equal("field", Assert.Single(record.fields).name);
        Assert.Empty(next.parameters);
        Assert.Throws<NotSupportedException>(() => ((IList<InteropParameter>)function.parameters).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<InteropField>)record.fields)[0] = new(new("other"), "other", new("integer"), 0, 0, null));
    }

    [Fact]
    public void ManagedEnumsAndExternalProjectionsCannotMutateTheFrozenNativeAbi()
    {
        List<ManagedEnumValue> values = [new("Original", "1")];
        ManagedEnumDefinition enumeration = new("Flags", ManagedIntegralKind.UInt32, values.ToImmutableArray());
        List<ExternalFieldProjection> fields = [new(new("native-field"), "managedField")];
        ExternalTypeProjection external = new(new("native-record"), "Consumer.Carrier", fields.ToImmutableArray());
        InteropModule interop = new("library", "windows-x64-msvc", 8, [], [], []);
        ManagedApiModel original = new("Example", "Api", ManagedApiStyle.Instance, interop, [], [])
        { enums = [enumeration], externalTypeProjections = [external] };
        values.Clear();
        fields[0] = new(new("different-field"), "changed");
        ManagedApiModel next = original with { enums = [], externalTypeProjections = [] };
        Assert.Same(interop, original.interop);
        Assert.Same(interop, next.interop);
        Assert.Equal("Original", Assert.Single(Assert.Single(original.enums).values).name);
        Assert.Equal("managedField", Assert.Single(Assert.Single(original.externalTypeProjections).fields).managedField);
        Assert.Empty(next.enums);
        Assert.Empty(next.externalTypeProjections);
        Assert.Throws<NotSupportedException>(() => ((IList<ManagedEnumValue>)enumeration.values).Clear());
    }
}
