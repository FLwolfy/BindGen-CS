using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core;
using BGCS.Core.Artifacts;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using BGCS.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using BGCS.CSharp.Validation;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class NeutralBackendTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable)]
    [InlineData(BindingImportMode.DllImport)]
    public async Task CallbackThroughIncompleteRecordAliasPreservesItsExactPointerPrototype(BindingImportMode mode)
    {
        InteropType nothing = new(new("void"), "void", NativeTypeCategory.Primitive, 0, 1) { primitiveKind = "Void" };
        InteropType incomplete = new(new("message-tag"), "NativeMessageTag", NativeTypeCategory.Record, null, null)
        { declarationName = "NativeMessageTag", recordKind = "Struct" };
        InteropType alias = new(new("message-alias"), "Message", NativeTypeCategory.Alias, null, null)
        { declarationName = "Message", elementType = incomplete.id };
        InteropType pointer = new(new("message-pointer"), "Message*", NativeTypeCategory.Pointer, 8, 8) { elementType = alias.id };
        InteropType signature = new(new("callback-prototype"), "void (Message*)", NativeTypeCategory.Function, null, null)
        { returnType = nothing.id, parameterTypes = [pointer.id], callingConvention = "C" };
        InteropType callback = new(new("callback-pointer"), "MessageCallback", NativeTypeCategory.Pointer, 8, 8)
        { elementType = signature.id };
        InteropFunction setter = new(new("set-message-callback"), "set_message_callback", nothing.id,
            [new(new("callback-argument"), "callback", callback.id)], "C");
        InteropModule module = new("messages", "windows-x64-msvc", 8,
            [nothing, incomplete, alias, pointer, signature, callback], [setter], [])
        { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        ManagedApiModel model = new DefaultManagedProjection().Project(module, new("Generated.Messages", "CallbackApi"), CancellationToken.None);
        MemorySink sink = new();
        EmissionRequest request = new("callback", sink) { importMode = mode };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);

        Assert.Equal("delegate* unmanaged[Cdecl]<NativeMessageTag*, void>", Assert.Single(plan.methods).parameterTypes[0]);
        Assert.Empty(plan.callScopedCallbacks);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        using CompiledSources compiled = Compile(sink);
        Type storage = compiled.assembly.GetType("Generated.Messages.NativeMessageTag", true)!;
        Assert.Empty(storage.GetFields());
        Assert.NotNull(compiled.assembly.GetType("Generated.Messages.CallbackApi"));
    }

    [Fact]
    public async Task EmptyNativeUnionHasExplicitOpaqueStorageThatCompilesAtItsDeclaredExtent()
    {
        ManagedApiModel original = Model();
        InteropType union = new(new("empty-union"), "NativeUnion", NativeTypeCategory.Record, 8, 8)
        { declarationName = "NativeUnion", recordKind = "Union" };
        InteropModule module = new("example", original.interop.targetId, 8, original.interop.types.Add(union), original.interop.functions, []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        ManagedApiModel model = new DefaultManagedProjection().Project(module, new("Generated.Example", "NativeApi"), CancellationToken.None);
        MemorySink sink = new();
        await new CSharpTestBackend().EmitAsync(model, new("unused", sink), CancellationToken.None);
        using CompiledSources compiled = Compile(sink);
        Type storage = compiled.assembly.GetType("Generated.Example.NativeUnion", true)!;
        Assert.True(storage.IsExplicitLayout);
        Assert.Equal(8, Marshal.SizeOf(storage));
        string declaration = sink.sources.Single(source => source.Key.EndsWith("/NativeUnion.g.cs", StringComparison.Ordinal)).Value;
        var layout = Assert.Single(CSharpSyntaxTree.ParseText(declaration).GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.AttributeSyntax>(), attribute => attribute.Name.ToString().EndsWith("StructLayout", StringComparison.Ordinal)
                || attribute.Name.ToString().EndsWith("StructLayoutAttribute", StringComparison.Ordinal));
        var extent = Assert.Single(layout.ArgumentList!.Arguments, argument => argument.NameEquals?.Name.Identifier.ValueText == "Size");
        Assert.Equal("8", extent.Expression.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SplitAndSingleFileStorageRetainFormattingAndCompileWithWarningsAsErrors(bool singleFile)
    {
        ManagedApiModel original = Model();
        InteropType integer = original.interop.types[0];
        InteropType record = new(new("sample-value"), "SampleValue", NativeTypeCategory.Record, 4, 4)
        { declarationName = "SampleValue", fields = [new(new("sample-field"), "Value", integer.id, 0, 0, null)] };
        InteropModule module = new("example", original.interop.targetId, 8, original.interop.types.Add(record), original.interop.functions, []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        ManagedApiModel model = new DefaultManagedProjection().Project(module, new("Generated.Example", "NativeApi"), CancellationToken.None);
        MemorySink sink = new();
        var artifacts = await new CSharpTestBackend().EmitAsync(model,
            new("unused", sink) { singleFile = singleFile, singleFileName = "SampleBindings.cs" }, CancellationToken.None);
        if (singleFile) Assert.Equal("SampleBindings.cs", Assert.Single(artifacts).relativePath);
        string source = Assert.Single(sink.sources.Values, value => value.Contains("partial struct SampleValue", StringComparison.Ordinal));
        string typeIndent = singleFile ? "    " : string.Empty;
        Assert.Matches("(?m)^" + typeIndent + @"public (?:unsafe )?partial struct SampleValue\r?$", source);
        Assert.Matches("(?m)^" + typeIndent + @"    public int Value;\r?$", source);
        sink.WriteText("ConsumerExtension.cs", "namespace Generated.Example { public partial struct SampleValue { public static int ExtendedValue() => 42; } }");
        using CompiledSources compiled = Compile(sink);
        Type storage = compiled.assembly.GetType("Generated.Example.SampleValue", true)!;
        Assert.Equal(4, Marshal.SizeOf(storage));
        Assert.Equal(42, storage.GetMethod("ExtendedValue")!.Invoke(null, null));
    }

    [Fact]
    public async Task PackedRecord_KeepsTheExactSizeAndUnalignedFieldOffset()
    {
        ManagedApiModel original = Model();
        InteropType octet = new(new("octet"), "unsigned char", NativeTypeCategory.Primitive, 1, 1) { primitiveKind = "UnsignedChar", isSigned = false };
        InteropType pointer = new(new("void-pointer"), "void*", NativeTypeCategory.Pointer, 8, 8) { elementType = original.interop.types[1].id };
        InteropType packed = new(new("packed"), "Packed", NativeTypeCategory.Record, 9, 1)
        { declarationName = "Packed", fields = [new(new("prefix"), "prefix", octet.id, 0, 0, null), new(new("value"), "value", pointer.id, 1, 8, null)] };
        InteropModule module = new("example", "windows-x64-msvc", 8, original.interop.types.AddRange(ImmutableArray.Create(octet, pointer, packed)), original.interop.functions, []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        MemorySink sink = new();
        var model = new DefaultManagedProjection().Project(module, new("Generated.Example", "NativeApi"), CancellationToken.None);
        await new CSharpTestBackend().EmitAsync(model, new("unused", sink), CancellationToken.None);
        using CompiledSources compiled = Compile(sink);
        Type carrier = compiled.assembly.GetType("Generated.Example.Packed", true)!;
        Assert.Equal(9, Marshal.SizeOf(carrier));
        Assert.Equal(1, Marshal.OffsetOf(carrier, "value").ToInt32());
        Type api = compiled.assembly.GetType("Generated.Example.NativeApi", true)!;
        using IDisposable context = (IDisposable)api.GetMethod("FromProvider")!.InvokeFactory([new StaticModuleProvider(Exports(false))])!;
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(4, true)]
    [InlineData(8, false)]
    [InlineData(8, true)]
    public async Task PointerArrays_UseExactNativeSlotsAndCheckedPointerAccess(
        int pointerSize,
        bool explicitName
    ) {
        ManagedApiModel original = Model();
        InteropType integer = original.interop.types[0];
        InteropType pointer = new(new("integer-pointer"), "int*", NativeTypeCategory.Pointer, pointerSize, pointerSize) { elementType = integer.id };
        InteropType array = new(new("pointer-array"), "PointerArray", NativeTypeCategory.Array, pointerSize * 3, pointerSize) { elementType = pointer.id, arrayLength = 3 };
        InteropModule module = new("example", pointerSize == 4 ? "windows-x86-msvc" : "windows-x64-msvc", pointerSize, original.interop.types.AddRange(ImmutableArray.Create(pointer, array)), original.interop.functions, []) { byteOrder = original.interop.byteOrder };
        ManagedApiModel model = new DefaultManagedProjection().Project(module, new("Generated.Example", "NativeApi")
        { names = explicitName ? ImmutableDictionary<string, string>.Empty.Add(array.id.value, "AddressSlots") : ImmutableDictionary<string, string>.Empty }, CancellationToken.None);
        MemorySink sink = new();
        await new CSharpTestBackend().EmitAsync(model, new("unused", sink), CancellationToken.None);
        string arrayName = new CSharpTestPlanner().Plan(model, new("unused", new MemorySink()), CancellationToken.None).storage.Single(storage => storage.nativeType == array.id).name;
        if (explicitName) Assert.Equal("AddressSlots", arrayName);
        sink.WriteText("Probe.cs", $$"""
            namespace Generated.Example;
            public static unsafe class PointerArrayProbe {
                public static nint RoundTrip(nint address) { {{arrayName}} value = default; value[1] = (int*)address; return (nint)value[1]; }
                public static void Bounds(int index) { {{arrayName}} value = default; _ = value[index]; }
            }
            """);
        using CompiledSources compiled = Compile(sink);
        Type carrier = compiled.assembly.GetType("Generated.Example." + arrayName, true)!;
        Assert.Equal(pointerSize * 3, Marshal.SizeOf(carrier));
        Type probe = compiled.assembly.GetType("Generated.Example.PointerArrayProbe", true)!;
        if (pointerSize == IntPtr.Size) Assert.Equal((nint)0x12345678, probe.GetMethod("RoundTrip")!.Invoke(null, [(nint)0x12345678]));
        else Assert.IsType<PlatformNotSupportedException>(Assert.Throws<TargetInvocationException>(() => probe.GetMethod("RoundTrip")!.Invoke(null, [(nint)1])).InnerException);
        Assert.IsType<ArgumentOutOfRangeException>(Assert.Throws<TargetInvocationException>(() => probe.GetMethod("Bounds")!.Invoke(null, [-1])).InnerException);
        Assert.IsType<ArgumentOutOfRangeException>(Assert.Throws<TargetInvocationException>(() => probe.GetMethod("Bounds")!.Invoke(null, [3])).InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeclaredUtf8Input_UsesScopedConversionWithPackageOrSourceRuntime(bool sourceRuntime)
    {
        InteropType octet = new(new("octet"), "char", NativeTypeCategory.Primitive, 1, 1) { primitiveKind = "Char", isSigned = true };
        InteropType pointer = new(new("text-pointer"), "char*", NativeTypeCategory.Pointer, 8, 8) { elementType = octet.id };
        InteropType integer = Model().interop.types[0];
        Evidence evidence = new(EvidenceLevel.UserAsserted, "test:explicit-native-text-contract");
        StringContract contract = new(NativeTextEncoding.Utf8, 1, NativeLengthUnit.CodeUnits, NativeParameterDirection.Input,
            new(NativeLifetimeKind.Call, null, evidence), evidence)
        { zeroTerminated = true, retainsPointer = false, allowsNull = false };
        InteropParameter parameter = new(new("text"), "text", pointer.id) { text = contract };
        InteropFunction function = new(new("utf8-size"), "utf8_size", integer.id, [parameter], "C");
        InteropModule interop = new("example", "windows-x64-msvc", 8, [octet, pointer, integer], [function], []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        ManagedApiModel model = new DefaultManagedProjection().Project(interop, new("Generated.Example", "NativeApi"), CancellationToken.None);
        Assert.Equal(parameter.id, Assert.Single(Assert.Single(model.methods).stringInputs));
        MemorySink sink = new();
        await new CSharpTestBackend().EmitAsync(model, new("unused", sink) { includeRuntimeSource = sourceRuntime, runtimeNamespace = sourceRuntime ? "Generated.Example.Runtime" : "BGCS.Runtime" }, CancellationToken.None);
        using CompiledSources compiled = Compile(sink);
        Type api = compiled.assembly.GetType("Generated.Example.NativeApi", true)!;
        var exports = new Dictionary<string, nint> { ["utf8_size"] = Utf8SizeAddress() };
        object provider = sourceRuntime ? compiled.assembly.GetType("Generated.Example.Runtime.StaticModuleProvider", true)!
            .GetConstructor([typeof(IEnumerable<KeyValuePair<string, nint>>), typeof(Action)])!.Invoke([exports, null]) : new StaticModuleProvider(exports);
        object context = api.GetMethod("FromProvider")!.InvokeFactory([provider])!;
        using ((IDisposable)context)
        {
            MethodInfo method = api.GetMethod("Utf8Size")!;
            Assert.Equal(typeof(string), Assert.Single(method.GetParameters()).ParameterType);
            Assert.Equal(5, method.Invoke(context, ["A🐈"]));
            Assert.IsType<ArgumentException>(Assert.Throws<TargetInvocationException>(() => method.Invoke(context, ["A\0B"])).InnerException);
        }
        InteropModule inferred = new("example", interop.targetId, 8, interop.types,
            [function with { parameters = [parameter with { text = contract with { evidence = new(EvidenceLevel.Inferred, "name-guess") } }] }], [])
        { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        Assert.Empty(Assert.Single(new DefaultManagedProjection().Project(inferred, new("Generated.Example", "NativeApi"), CancellationToken.None).methods).stringInputs);
    }

    private static unsafe nint Utf8SizeAddress() => (nint)(delegate* unmanaged[Cdecl]<byte*, int>)&Utf8Size;

    [Theory]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(4, false)]
    [InlineData(4, true)]
    public async Task ExplicitWideText_UsesTheNativeWidthAndAlignmentWithPackageOrSourceRuntime(
        int width,
        bool sourceRuntime
    ) {
        InteropType unit = new(new("wide-unit"), "wchar_t", NativeTypeCategory.Primitive, width, width) { primitiveKind = "WChar" };
        InteropType aligned = new(new("aligned-unit"), "AlignedWide", NativeTypeCategory.Alias, width, 64) { elementType = unit.id };
        InteropType pointer = new(new("wide-pointer"), "const AlignedWide*", NativeTypeCategory.Pointer, 8, 8) { elementType = aligned.id };
        InteropType integer = new(new("integer"), "unsigned int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "UnsignedInt", isSigned = false };
        Evidence evidence = new(EvidenceLevel.UserAsserted, "fixture:wide-text");
        StringContract text = new(width == 2 ? NativeTextEncoding.Utf16 : NativeTextEncoding.Utf32, width, NativeLengthUnit.CodeUnits, NativeParameterDirection.Input,
            new(NativeLifetimeKind.Call, null, evidence), evidence)
        { byteOrder = NativeTextByteOrder.Native, zeroTerminated = true, retainsPointer = false, allowsNull = false };
        InteropParameter parameter = new(new("text"), "text", pointer.id) { text = text };
        InteropFunction first = new(new("first-unit"), "first_unit", integer.id, [parameter], "C");
        InteropFunction last = new(new("next-unit"), "next_unit", integer.id, [parameter], "C");
        InteropModule module = new("wide", "windows-x64-msvc", 8, [unit, aligned, pointer, integer], [first, last], []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        ManagedApiModel model = new DefaultManagedProjection().Project(module, new("Generated.Wide", "WideApi"), CancellationToken.None);
        Assert.All(model.methods, method => Assert.Equal(parameter.id, Assert.Single(method.stringInputs)));
        MemorySink sink = new();
        await new CSharpTestBackend().EmitAsync(model, new("unused", sink) { includeRuntimeSource = sourceRuntime, runtimeNamespace = sourceRuntime ? "Generated.Wide.Runtime" : "BGCS.Runtime" }, CancellationToken.None);
        using CompiledSources compiled = Compile(sink);
        Type api = compiled.assembly.GetType("Generated.Wide.WideApi", true)!;
        Dictionary<string, nint> exports = WideAddresses(width);
        object provider = sourceRuntime ? compiled.assembly.GetType("Generated.Wide.Runtime.StaticModuleProvider", true)!
            .GetConstructor([typeof(IEnumerable<KeyValuePair<string, nint>>), typeof(Action)])!.Invoke([exports, null]) : new StaticModuleProvider(exports);
        using IDisposable context = (IDisposable)api.GetMethod("FromProvider")!.InvokeFactory([provider])!;
        Assert.Equal((uint)65, api.GetMethod("FirstUnit")!.Invoke(context, ["AB"]));
        Assert.Equal(width == 2 ? (uint)0xd83d : 0x1f408U, api.GetMethod("NextUnit")!.Invoke(context, ["A🐈"]));
        Assert.IsType<EncoderFallbackException>(Assert.Throws<TargetInvocationException>(() => api.GetMethod("FirstUnit")!.Invoke(context, ["\ud800"])).InnerException);
        InteropModule unknown = new("wide", module.targetId, 8, module.types, [first with { parameters = [parameter with { text = text with { byteOrder = NativeTextByteOrder.Unknown } }] }], []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        Assert.Empty(Assert.Single(new DefaultManagedProjection().Project(unknown, new("Generated.Wide", "WideApi"), CancellationToken.None).methods).stringInputs);
    }

    private static unsafe Dictionary<string, nint> WideAddresses(int width) => new()
    {
        ["first_unit"] = width == 2 ? (nint)(delegate* unmanaged[Cdecl]<ushort*, uint>)&Utf16First : (nint)(delegate* unmanaged[Cdecl]<uint*, uint>)&Utf32First,
        ["next_unit"] = width == 2 ? (nint)(delegate* unmanaged[Cdecl]<ushort*, uint>)&Utf16Next : (nint)(delegate* unmanaged[Cdecl]<uint*, uint>)&Utf32Next
    };

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] private static unsafe uint Utf16First(ushort* value) => (nuint)value % 64 == 0 ? value[0] : uint.MaxValue;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] private static unsafe uint Utf32First(uint* value) => (nuint)value % 64 == 0 ? value[0] : uint.MaxValue;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] private static unsafe uint Utf16Next(ushort* value) => value[1];
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] private static unsafe uint Utf32Next(uint* value) => value[1];

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe int Utf8Size(byte* value)
    {
        int length = 0;
        while (value[length] != 0) length++;
        return length;
    }

    [Fact]
    public async Task FlexibleArrayTail_PreservesBaseStorageAndExposesOnlyAnExplicitRawPointer()
    {
        ManagedApiModel original = Model();
        InteropType integer = original.interop.types[0];
        InteropType flexible = new(new("flexible"), "int[]", NativeTypeCategory.Array, null, 4) { elementType = integer.id };
        InteropType packet = new(new("packet"), "Packet", NativeTypeCategory.Record, 4, 4)
        {
            declarationName = "Packet",
            recordKind = "Struct",
            fields = [new(new("count"), "count", integer.id, 0, 0, null), new(new("data"), "data", flexible.id, 4, 32, null)]
        };
        InteropModule interop = new("example", original.interop.targetId, 8, original.interop.types.AddRange(ImmutableArray.Create(flexible, packet)), original.interop.functions, []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        ManagedApiModel model = new DefaultManagedProjection().Project(interop, new("Generated.Example", "NativeApi"), CancellationToken.None);
        MemorySink sink = new();
        await new CSharpTestBackend().EmitAsync(model, new("unused", sink), CancellationToken.None);
        string source = sink.sources["Types/Packet.g.cs"];
        Assert.Contains("GetDataPointer", source);
        Assert.DoesNotContain("InlineArray", source);
        Assert.DoesNotContain("Span<", source);
        sink.sources.Add("Harness.cs", "namespace Generated.Example; public static unsafe class Harness { public static int Offset() { byte* memory = stackalloc byte[20]; return (int)((byte*)Packet.GetDataPointer((Packet*)memory) - memory); } }");
        using CompiledSources compiled = Compile(sink);
        Assert.Equal(4, Marshal.SizeOf(compiled.assembly.GetType("Generated.Example.Packet", true)!));
        Assert.Equal(4, compiled.assembly.GetType("Generated.Example.Harness", true)!.GetMethod("Offset")!.Invoke(null, []));
    }

    [Fact]
    public async Task UnspecifiedCPrototype_RejectsBeforeWritingAnyArtifact()
    {
        ManagedApiModel model = Model();
        InteropModule interop = new("example", model.interop.targetId, model.interop.pointerSize, model.interop.types,
            [model.interop.functions[0] with { hasPrototype = false }], [])
        { byteOrder = model.interop.byteOrder };
        model = new DefaultManagedProjection().Project(interop, new("Generated.Example", "NativeApi"), CancellationToken.None);
        MemorySink sink = new();
        await Assert.ThrowsAsync<CSharpPlanningException>(async () => await new CSharpTestBackend().EmitAsync(model, new("unused", sink), CancellationToken.None));
        Assert.Empty(sink.sources);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FunctionTable_CompilesInvokesIndependentInstancesAndClosesCalls(bool singleFile)
    {
        MemorySink sink = new();
        var artifacts = await new CSharpTestBackend().EmitAsync(Model(), new("unused", sink) { singleFile = singleFile }, CancellationToken.None);
        foreach (ArtifactDescriptor artifact in artifacts)
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sink.sources[artifact.relativePath]))), artifact.digest.value);
        Assert.Equal(singleFile ? 1 : 3, artifacts.Length);
        using CompiledSources compiled = Compile(sink);
        Type api = compiled.assembly.GetType("Generated.Example.NativeApi", true)!;
        int released = 0;
        object first = api.GetMethod("FromProvider")!.InvokeFactory([new StaticModuleProvider(Exports(false), () => released++)])!;
        object second = api.GetMethod("FromProvider")!.InvokeFactory([new StaticModuleProvider(Exports(true), () => released++)])!;
        object raw = api.GetProperty("Raw")!.GetValue(first)!;
        MethodInfo add = raw.GetType().GetMethod("Add")!;
        Assert.Equal(7, add.Invoke(raw, [3, 4]));
        Assert.Equal(7, api.GetMethod("Add")!.Invoke(first, [3, 4]));
        object other = api.GetProperty("Raw")!.GetValue(second)!;
        Assert.Equal(12, other.GetType().GetMethod("Add")!.Invoke(other, [3, 4]));
        raw.GetType().GetMethod("Notify")!.Invoke(raw, []);
        ((IDisposable)first).Dispose();
        Assert.Equal(1, released);
        Assert.IsType<ObjectDisposedException>(Assert.Throws<TargetInvocationException>(() => add.Invoke(raw, [1, 2])).InnerException);
        Assert.Equal(8, other.GetType().GetMethod("Add")!.Invoke(other, [1, 2]));
        await ((IAsyncDisposable)second).DisposeAsync();
        Assert.Equal(2, released);
    }

    [Fact]
    public async Task MissingExport_FailsConstructionAndReleasesTheOwnedProvider()
    {
        MemorySink sink = new();
        await new CSharpTestBackend().EmitAsync(Model(), new("unused", sink), CancellationToken.None);
        using CompiledSources compiled = Compile(sink);
        Type api = compiled.assembly.GetType("Generated.Example.NativeApi", true)!;
        int released = 0;
        StaticModuleProvider provider = new(Exports(false).Where(pair => pair.Key == "add"), () => released++);
        TargetInvocationException failure = Assert.Throws<TargetInvocationException>(() => api.GetMethod("FromProvider")!.InvokeFactory([provider]));
        Assert.IsType<EntryPointNotFoundException>(failure.InnerException);
        Assert.Equal(1, released);
    }

    [Theory]
    [InlineData(BindingImportMode.FunctionTable)]
    [InlineData(BindingImportMode.DllImport)]
    public async Task Storage_CompilesExactNestedAlignmentArraysAndHighUnsignedEnum(BindingImportMode mode)
    {
        MemorySink sink = new();
        ManagedApiModel model = ModelWithStorage();
        await new CSharpTestBackend().EmitAsync(model, new("unused", sink) { importMode = mode }, CancellationToken.None);
        using CompiledSources compiled = Compile(sink);
        Type outer = compiled.assembly.GetType("Generated.Example.Outer", true)!;
        Assert.Equal(96, Marshal.SizeOf(outer));
        Assert.Equal(32, Marshal.OffsetOf(outer, "nested").ToInt64());
        Assert.Equal(64, Marshal.OffsetOf(outer, "suffix").ToInt64());
        Type wideEnum = compiled.assembly.GetType("Generated.Example.Flags", true)!;
        Assert.Equal(ulong.MaxValue, Convert.ToUInt64(Enum.Parse(wideEnum, "all")));
        Type array = compiled.assembly.GetTypes().Single(type => type.Name.StartsWith("NativeArray_", StringComparison.Ordinal));
        Assert.Equal(12, Marshal.SizeOf(array));
    }

    [Fact]
    public async Task Identifiers_AreValidatedAndKeywordsEscapedBeforeWriting()
    {
        ManagedApiModel model = Model();
        model = model with { methods = model.methods.SetItem(0, model.methods[0] with { parameterNames = ["class", "m_owner"] }) };
        MemorySink sink = new();
        await new CSharpTestBackend().EmitAsync(model, new("library\"with\\escapes", sink), CancellationToken.None);
        using CompiledSources compiled = Compile(sink);
        Assert.NotNull(compiled.assembly.GetType("Generated.Example.NativeApi"));
        MemorySink rejected = new();
        await Assert.ThrowsAsync<ArgumentException>(async () => await new CSharpTestBackend().EmitAsync(model with { name = "Api; malicious" }, new("unused", rejected), CancellationToken.None));
        Assert.Empty(rejected.sources);
    }

    [Fact]
    public async Task UnverifiedByValueRecord_RejectsBeforeWritingAnyCandidateArtifact()
    {
        ManagedApiModel model = ModelWithStorage();
        InteropType outer = model.interop.types.Single(type => type.declarationName == "Outer");
        InteropFunction bad = new(new("bad"), "bad", outer.id, [], "C");
        InteropModule interop = new("example", model.interop.targetId, 8, model.interop.types, [bad], []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        model = new DefaultManagedProjection().Project(interop, new("Generated.Example", "NativeApi"), CancellationToken.None);
        MemorySink sink = new();
        var failure = await Assert.ThrowsAsync<CSharpPlanningException>(async () => await new CSharpTestBackend().EmitAsync(model, new("unused", sink), CancellationToken.None));
        Assert.Contains("complete plan", failure.Message);
        Assert.Empty(sink.sources);
    }

    internal static ManagedApiModel Model()
    {
        InteropType integer = new(new("int"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropType nothing = new(new("void"), "void", NativeTypeCategory.Primitive, 0, 1) { primitiveKind = "Void" };
        InteropFunction add = new(new("add"), "add", integer.id, [new(new("left"), "left", integer.id), new(new("right"), "right", integer.id)], "C");
        InteropFunction notify = new(new("notify"), "notify", nothing.id, [], "C");
        return new DefaultManagedProjection().Project(new("example", "windows-x64-msvc", 8, [integer, nothing], [add, notify], []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian },
            new("Generated.Example", "NativeApi"), CancellationToken.None);
    }

    internal static ManagedApiModel ModelWithStorage()
    {
        ManagedApiModel original = Model();
        InteropType integer = original.interop.types[0];
        InteropType octet = new(new("byte"), "unsigned char", NativeTypeCategory.Primitive, 1, 1) { primitiveKind = "UnsignedChar", isSigned = false };
        InteropType aligned = new(new("aligned"), "Aligned", NativeTypeCategory.Record, 32, 32)
        { declarationName = "Aligned", fields = [new(new("value"), "value", integer.id, 0, 0, null)] };
        InteropType outer = new(new("outer"), "Outer", NativeTypeCategory.Record, 96, 32)
        { declarationName = "Outer", fields = [new(new("prefix"), "prefix", octet.id, 0, 0, null), new(new("nested"), "nested", aligned.id, 32, 256, null), new(new("suffix"), "suffix", integer.id, 64, 512, null)] };
        InteropType array = new(new("array"), "int[3]", NativeTypeCategory.Array, 12, 4) { elementType = integer.id, arrayLength = 3 };
        InteropType unsigned = new(new("ulong"), "unsigned long long", NativeTypeCategory.Primitive, 8, 8) { primitiveKind = "UnsignedLongLong", isSigned = false };
        InteropType flags = new(new("flags"), "Flags", NativeTypeCategory.Enumeration, 8, 8)
        { declarationName = "Flags", elementType = unsigned.id, enumValues = [new(new("all"), "all", "~0ull", ulong.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture))] };
        InteropModule interop = new("example", "windows-x64-msvc", 8, original.interop.types.AddRange(ImmutableArray.Create(octet, aligned, outer, array, unsigned, flags)), original.interop.functions, []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        return new DefaultManagedProjection().Project(interop, new("Generated.Example", "NativeApi"), CancellationToken.None);
    }

    private static unsafe IEnumerable<KeyValuePair<string, nint>> Exports(bool alternate) => new Dictionary<string, nint>
    {
        ["add"] = alternate ? (nint)(delegate* unmanaged[Cdecl]<int, int, int>)&AddAlternate : (nint)(delegate* unmanaged[Cdecl]<int, int, int>)&Add,
        ["notify"] = (nint)(delegate* unmanaged[Cdecl]<void>)&Notify
    };

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Add(
        int left,
        int right
    ) => left + right;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int AddAlternate(
        int left,
        int right
    ) => left + right + 5;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Notify() { }

    private static CompiledSources Compile(MemorySink sink)
    {
        string[] paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var references = paths.Append(typeof(NativeModuleSession).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path));
        CSharpCompilation compilation = CSharpCompilation.Create("Neutral.Generated." + Guid.NewGuid().ToString("N"),
            sink.sources.Select(pair => CSharpSyntaxTree.ParseText(pair.Value, new CSharpParseOptions(LanguageVersion.CSharp14), pair.Key)), references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error));
        using MemoryStream stream = new();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics.Select(diagnostic => diagnostic.ToString())));
        stream.Position = 0;
        AssemblyLoadContext context = new("BGCS.NeutralBackend.Test", isCollectible: true);
        return new(context, context.LoadFromStream(stream));
    }

    internal sealed class MemorySink : IArtifactSink
    {
        internal readonly Dictionary<string, string> sources = new(StringComparer.Ordinal);
        public string WriteText(
            string relativePath,
            string content
        ) { sources.Add(relativePath, content); return relativePath; }
    }

    private sealed record CompiledSources(
        AssemblyLoadContext context,
        Assembly assembly
    ) : IDisposable
    {
        public void Dispose() => context.Unload();
    }
}
