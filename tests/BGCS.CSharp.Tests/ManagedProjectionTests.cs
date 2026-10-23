using System;
using System.Threading;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.CSharp.Projection;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class ManagedProjectionTests
{
    [Fact]
    public void Projection_UsesCompilerWidthAndSignednessForFrameworkIdentity()
    {
        InteropType nativeLong = new(new("long"), "long", NativeTypeCategory.Primitive, 8, 8) { primitiveKind = "Long", isSigned = true };
        InteropModule interop = new("example", "linux-x64-gnu", 8, [nativeLong], [], []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        ManagedApiModel result = new DefaultManagedProjection().Project(interop, new("Example", "ExampleApi"), CancellationToken.None);
        Assert.Equal("System.Int64", Assert.Single(result.types).frameworkType);
        Assert.Equal(ManagedApiStyle.Instance, result.style);
        Assert.Same(interop, result.interop);
    }

    [Fact]
    public void ExplicitNameMappings_ResolveNativeNamesThatWouldOtherwiseConflict()
    {
        InteropType first = new(new("a"), "a", NativeTypeCategory.Record, 4, 4) { declarationName = "same_name" };
        InteropType second = new(new("b"), "b", NativeTypeCategory.Record, 4, 4) { declarationName = "SameName" };
        InteropModule interop = new("example", "linux-x64-gnu", 8, [first, second], [], []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        Assert.Throws<InvalidOperationException>(() => new DefaultManagedProjection().Project(interop, new("Example", "ExampleApi"), CancellationToken.None));
        ManagedProjectionRequest request = new("Example", "ExampleApi") { names = System.Collections.Immutable.ImmutableDictionary<string, string>.Empty.Add("same_name", "FirstType") };
        var result = new DefaultManagedProjection().Project(interop, request, CancellationToken.None);
        Assert.Equal("FirstType", result.types[0].name);
        Assert.Equal("SameName", result.types[1].name);
    }

    [Fact]
    public void Projection_DoesNotInventFriendlyConversionsForRawPointers()
    {
        InteropType pointer = new(new("pointer"), "char*", NativeTypeCategory.Pointer, 8, 8);
        InteropFunction function = new(new("function"), "read_text", pointer.id, [], "C");
        InteropModule interop = new("example", "linux-x64-gnu", 8, [pointer], [function], []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        var result = new DefaultManagedProjection().Project(interop, new("Example", "ExampleApi"), CancellationToken.None);
        Assert.Null(Assert.Single(result.types).frameworkType);
        Assert.Equal("ReadText", Assert.Single(result.methods).name);
        Assert.Equal(pointer.id, result.interop.functions[0].returnType);
    }

    [Fact]
    public void NativeBooleanValuesHaveFriendlyConversionsWithoutReplacingRawStorage()
    {
        InteropType boolean = new(new("bool"), "_Bool", NativeTypeCategory.Primitive, 1, 1) { primitiveKind = "Bool", isSigned = false };
        InteropType alias = new(new("flag"), "Flag", NativeTypeCategory.Alias, 1, 1) { elementType = boolean.id };
        InteropType pointer = new(new("pointer"), "Flag*", NativeTypeCategory.Pointer, 8, 8) { elementType = alias.id };
        InteropFunction function = new(new("function"), "negate", alias.id, [new(new("value"), "value", alias.id), new(new("address"), "address", pointer.id)], "C");
        InteropModule interop = new("example", "windows-x64-msvc", 8, [boolean, alias, pointer], [function], []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        ManagedApiModel result = new DefaultManagedProjection().Project(interop, new("Example", "ExampleApi"), CancellationToken.None);
        Assert.Equal("System.Byte", result.types[0].frameworkType);
        ManagedMember method = Assert.Single(result.methods);
        Assert.Equal("bool", Assert.Single(method.valueProjections).projection.managedType);
        Assert.Equal(new StableId("value"), method.valueProjections[0].nativeParameter);
        Assert.Equal("bool", method.returnProjection!.managedType);
        Assert.Equal("{value} != 0", method.returnProjection.nativeToManagedExpression);
        Assert.Same(interop, result.interop);
        Assert.Empty(method.valuePointers);
    }

    [Fact]
    public void IntegerByteValuesAreNeverAssumedToBeBooleans()
    {
        InteropType integer = new(new("byte"), "unsigned char", NativeTypeCategory.Primitive, 1, 1) { primitiveKind = "UChar", isSigned = false };
        InteropFunction function = new(new("function"), "identity", integer.id, [new(new("value"), "value", integer.id)], "C");
        InteropModule interop = new("example", "windows-x64-msvc", 8, [integer], [function], []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        ManagedMember method = Assert.Single(new DefaultManagedProjection().Project(interop, new("Example", "ExampleApi"), CancellationToken.None).methods);
        Assert.Empty(method.valueProjections);
        Assert.Null(method.returnProjection);
    }

    [Theory]
    [InlineData("int(int)")]
    [InlineData("")]
    public void AnonymousCallbackTypesUseAvailableSpellingOrStableFallback(string spelling)
    {
        InteropType function = new(new("anonymous-callback"), spelling, NativeTypeCategory.Function, null, null)
        { declarationName = "", sourceQualifiedName = "" };
        InteropModule interop = new("example", "windows-x64-msvc", 8, [function], [], []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        ManagedType type = Assert.Single(new DefaultManagedProjection().Project(interop, new("Example", "ExampleApi")
        { naming = new() { callbacks = new() { prefix = "Callback" } } }, CancellationToken.None).types);
        Assert.StartsWith("Callback", type.name, StringComparison.Ordinal);
        Assert.True(type.name.Length > "Callback".Length);
    }
}
