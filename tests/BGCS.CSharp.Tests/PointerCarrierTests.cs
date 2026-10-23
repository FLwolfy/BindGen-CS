using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Analysis.Safety;
using BGCS.Core;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using BGCS.CSharp.Validation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class PointerCarrierTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task AddressViewsPreserveRawAbiConstFieldsArraysAndWholeSurfaceGuards(
        BindingImportMode mode,
        bool sourceRuntime
    ) {
        ManagedApiModel model = Model(sourceRuntime);
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native", sink)
        {
            importMode = mode,
            includeRuntimeSource = sourceRuntime,
            singleFile = sourceRuntime,
            runtimeNamespace = sourceRuntime ? "Generated.Addresses.Runtime" : "BGCS.Runtime"
        };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        Assert.Equal(4, plan.pointerCarriers.Length);
        Assert.Equal(2, plan.pointerFunctions.Length);
        Assert.All(plan.methods, method => Assert.DoesNotContain(plan.pointerCarriers, carrier => method.returnType == carrier.name
            || method.returnType.EndsWith("." + carrier.name, StringComparison.Ordinal)));
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        string text = string.Join("\n", sink.sources.Values);
        Assert.Contains("DangerousArray_values", text);
        Assert.Contains("DangerousField_next", text);
        Assert.Contains("ref readonly", text);
        Assert.Contains("ReadOnlySpan<int>", text);
        Assert.DoesNotContain("Unsafe.As<", text);
        string prefix = sourceRuntime ? "AddressApi." : string.Empty;
        string context = sourceRuntime ? "AddressApiContext" : "AddressApi";
        string consumer = $$"""
            namespace Generated.Addresses;
            public static unsafe class Consumer {
                public static int Use({{context}} context,{{prefix}}NodeAddress address) {
                    address.DangerousField_value()=40;
                    address.DangerousArray_values()[0]=2;
                    return address.ReadAddress(context)+address.DangerousArray_values()[0];
                }
            }
            """;
        if (mode == BindingImportMode.LibraryImport) await ValueReceiverTests.CompileWithSdkAsync(sink, consumer, sourceRuntime);
        else
        {
            CSharpCompilation compilation = CSharpCompilation.Create("Addresses_" + Guid.NewGuid().ToString("N"),
                sink.sources.Select(pair => CSharpSyntaxTree.ParseText(pair.Value, new(LanguageVersion.CSharp14), pair.Key))
                    .Append(CSharpSyntaxTree.ParseText(consumer, new(LanguageVersion.CSharp14))),
                ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                    .Append(typeof(BGCS.Runtime.NativeModuleSession).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path)),
                new(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error));
            using MemoryStream image = new();
            var result = compilation.Emit(image);
            Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        }
        foreach (string mutation in new[] { "this.m_handle == 0", "checked(index *", "ReadOnlySpan<int>", "this.m_session.AcquireCall()", "(nint)__bgcsAddressResult" })
        {
            var changed = sink.sources.Select(pair => (pair.Key, pair.Value.Replace(mutation, mutation switch
            {
                "this.m_handle == 0" => "this.m_handle != 0",
                "checked(index *" => "unchecked(index *",
                "ReadOnlySpan<int>" => "Span<int>",
                "this.m_session.AcquireCall()" => "this.m_session.AcquireResource()",
                _ => "(nint)0"
            }, StringComparison.Ordinal))).ToArray();
            Assert.Contains(changed, pair => pair.Item2 != sink.sources[pair.Key]);
            Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan, changed, "native"));
        }
    }

    [Theory]
    [InlineData("const-removal")]
    [InlineData("double-const-removal")]
    [InlineData("alias-change")]
    [InlineData("owned-result")]
    [InlineData("duplicate-pointer")]
    [InlineData("type-collision")]
    [InlineData("method-collision")]
    public void AddressProjectionCannotInventConstConversionsOrReplaceTrackedResources(string fault)
    {
        ManagedApiModel model = Model(false);
        if (fault == "const-removal") model = model with { pointerFunctions = [new(new("read"), [new(new("self"), "ConstNodeAddress")]) { name = "ReadAddress" }] };
        if (fault == "double-const-removal") model = model with
        {
            pointerFunctions = [new(new("get"), []) { name = "GetAddress", returns = "WrongDouble" }],
            pointerCarriers = model.pointerCarriers.Add(new(new("const-pointer-pointer"), "WrongDouble"))
        };
        if (fault == "alias-change") model = model with
        {
            pointerFunctions = [new(new("read"), [new(new("self"), "AliasAddress")]) { name = "ReadAddress" }],
            pointerCarriers = model.pointerCarriers.Add(new(new("pointer-alias"), "AliasAddress"))
        };
        if (fault == "owned-result") model = model with
        {
            interop = new(model.interop.libraryId, model.interop.targetId, model.interop.pointerSize, model.interop.types,
            model.interop.functions.Select(function => function.id == new StableId("get")
            ? function with { returnOwnership = new(NativeOwnershipKind.Borrowed, new(NativeLifetimeKind.Module, null, new(EvidenceLevel.UserAsserted, "fixture:tracked")), new(EvidenceLevel.UserAsserted, "fixture:tracked")) } : function).ToImmutableArray(), [])
            { byteOrder = model.interop.byteOrder }
        };
        if (fault == "duplicate-pointer") model = model with { pointerCarriers = model.pointerCarriers.Add(new(new("pointer"), "DuplicateAddress")) };
        if (fault == "type-collision") model = model with { pointerCarriers = [new(new("pointer"), "Node")] };
        if (fault == "method-collision") model = model with { pointerFunctions = model.pointerFunctions.Add(new(new("read"), [new(new("self"), "NodeAddress")]) { name = "ReadAddress" }) };
        if (fault is "duplicate-pointer" or "type-collision" or "method-collision")
            Assert.Throws<ArgumentException>(() => new CSharpTestPlanner().Plan(model, new("native", new NeutralBackendTests.MemorySink())));
        else Assert.Throws<CSharpPlanningException>(() => new CSharpTestPlanner().Plan(model, new("native", new NeutralBackendTests.MemorySink())));
    }

    [Fact]
    public void NativePointerGraphPreservesEveryLevelAndOpaqueAliasIdentity()
    {
        NativePointerShapeReader reader = new(Module());
        Assert.True(reader.CanConvert(new("pointer"), new("const-pointer")));
        Assert.False(reader.CanConvert(new("const-pointer"), new("pointer")));
        Assert.False(reader.CanConvert(new("pointer-pointer"), new("const-pointer-pointer")));
        Assert.False(reader.CanConvert(new("pointer-alias"), new("pointer")));
        Assert.Equal(2, reader.Read(new("pointer-pointer"))!.depth);
        Assert.Null(reader.Read(new("int")));
    }

    [Fact]
    public void OpaqueAddressesHaveNoInventedDereferenceOrStorageExtent()
    {
        ManagedApiModel model = Model(false);
        InteropModule module = new(model.interop.libraryId, model.interop.targetId, model.interop.pointerSize, model.interop.types.Select(type => type.id == new StableId("record")
            ? type with { size = null, alignment = null, fields = [] } : type).ToImmutableArray(), model.interop.functions, [])
        { byteOrder = model.interop.byteOrder };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(Model(false, module), new("native", new NeutralBackendTests.MemorySink()));
        Assert.All(plan.pointerCarriers.Where(carrier => carrier.depth == 1), carrier => { Assert.Null(carrier.elementSize); Assert.Empty(carrier.fields); });
    }

    [Fact]
    public void NamedVoidPointerAliasesRemainOpaqueAndDistinct()
    {
        InteropModule original = Module();
        InteropModule module = new(original.libraryId, original.targetId, original.pointerSize, original.types.AddRange(ImmutableArray.Create<InteropType>(
            new InteropType(new("void"), "void", NativeTypeCategory.Primitive, 0, 1) { primitiveKind = "Void" },
            new InteropType(new("void-pointer"), "void*", NativeTypeCategory.Pointer, 8, 8) { elementType = new("void") },
            new InteropType(new("void-alias"), "NativeContext", NativeTypeCategory.Alias, 8, 8) { elementType = new("void-pointer") })), original.functions, [])
        { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        ManagedApiModel model = new DefaultManagedProjection().Project(module, new("Generated", "OpaqueApi")
        { pointerCarriers = [new("NativeContext", "ContextAddress")] }, CancellationToken.None);
        CSharpPointerCarrierPlan carrier = Assert.Single(new CSharpTestPlanner().Plan(model, new("native", new NeutralBackendTests.MemorySink())).pointerCarriers);
        Assert.Null(carrier.elementSize); Assert.Empty(carrier.fields);
        Assert.False(new NativePointerShapeReader(module).CanConvert(new("void-alias"), new("void-pointer")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WholeRecordViewsCannotEraseNestedConstPointersOrVolatileStorage(bool volatileField)
    {
        InteropModule original = Module();
        ImmutableArray<InteropType> types = original.types.Add(new(new("volatile-int"), "volatile int", NativeTypeCategory.Qualified, 4, 4)
        { qualifier = "Volatile", elementType = new("int") }).Select(type => type.id != new StableId("record") ? type : type with
        {
            fields = type.fields.Select(field => volatileField && field.id == new StableId("value") ? field with { type = new("volatile-int") }
            : !volatileField && field.id == new StableId("next") ? field with { type = new("const-pointer") } : field).ToImmutableArray()
        }).ToImmutableArray();
        InteropModule module = new(original.libraryId, original.targetId, original.pointerSize, types, original.functions, []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        CSharpPointerCarrierPlan carrier = new CSharpTestPlanner().Plan(Model(false, module), new("native", new NeutralBackendTests.MemorySink()))
            .pointerCarriers.Single(item => item.name == "NodeAddress");
        Assert.Null(carrier.elementSize);
        Assert.False(carrier.exposesTypedPointer);
        if (volatileField) Assert.DoesNotContain(carrier.fields, field => field.name == "value");
        else Assert.Equal("ConstNodeAddress", Assert.Single(carrier.fields, field => field.name == "next").carrier);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AddressDocumentationOverrideRemainsInertEscapedProse(bool nested)
    {
        const string prose = "</summary>\n<include file=\"unexpected.xml\" /> &\npublic class Injected {}";
        ManagedApiModel model = Model(nested, documentation: prose);
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native", sink);
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        Assert.Equal(prose, Assert.Single(plan.pointerCarriers, carrier => carrier.name == "NodeAddress").documentation);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        string text = string.Join("\n", sink.sources.Values);
        Assert.Contains("&lt;include", text);
        Assert.Contains("&amp;", text);
        Assert.Contains("does not own or retain native memory", text);
        var trees = sink.sources.Select(source => CSharpSyntaxTree.ParseText(source.Value, new(LanguageVersion.CSharp14, documentationMode: DocumentationMode.Diagnose))).ToArray();
        Assert.DoesNotContain(trees.SelectMany(tree => tree.GetDiagnostics()), diagnostic => diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error);
        Assert.DoesNotContain(trees.SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>()), declaration => declaration.Identifier.ValueText == "Injected");
        new EmittedInteropContractReader().Verify(plan, sink.sources.Select(source => (source.Key, source.Value)), "native");
    }

    [Theory]
    [InlineData("unknown-parameter")]
    [InlineData("unmapped-parameter")]
    [InlineData("reserved-member")]
    [InlineData("private-field")]
    [InlineData("carrier-name")]
    public void AddressReceiverRequiresAnExactMappedParameterAndDistinctMember(string fault)
    {
        ManagedApiModel model = Model(false);
        ManagedPointerFunction operation = model.pointerFunctions[0];
        if (fault == "unknown-parameter") operation = operation with { receiver = new("missing") };
        if (fault == "unmapped-parameter") operation = operation with { parameters = [], receiver = new("self"), returns = "NodeAddress" };
        if (fault == "reserved-member") operation = operation with { name = "Handle" };
        if (fault == "private-field") operation = operation with { name = "m_handle" };
        if (fault == "carrier-name") operation = operation with { name = "NodeAddress" };
        NeutralBackendTests.MemorySink sink = new();
        Assert.ThrowsAny<Exception>(() => new CSharpTestPlanner().Plan(model with { pointerFunctions = [operation] }, new("native", sink)));
        Assert.Empty(sink.sources);
    }

    [Fact]
    public void AddressCarrierNameCannotCollideWithItsPrivateStorageField()
    {
        ManagedApiModel model = Model(false) with { pointerCarriers = [new(new("pointer"), "m_handle")], pointerFunctions = [] };
        NeutralBackendTests.MemorySink sink = new();
        Assert.Throws<ArgumentException>(() => new CSharpTestPlanner().Plan(model, new("native", sink)));
        Assert.Empty(sink.sources);
    }

    [Theory]
    [InlineData("context-null-check")]
    [InlineData("receiver-conversion")]
    [InlineData("context-forward")]
    public async Task AddressReceiverFinalContractRejectsContextOrReceiverTampering(string fault)
    {
        ManagedApiModel model = Model(false);
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native", sink);
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        string from = fault switch { "context-null-check" => "global::System.ArgumentNullException.ThrowIfNull(context);", "receiver-conversion" => "context.ReadAddress(this)", _ => "return context.ReadAddress(this);" };
        string to = fault switch { "context-null-check" => "", "receiver-conversion" => "context.ReadAddress(default)", _ => "return 0;" };
        var changed = sink.sources.Select(source => (source.Key, source.Value.Replace(from, to, StringComparison.Ordinal))).ToArray();
        Assert.Contains(changed, source => source.Item2 != sink.sources[source.Key]);
        Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan, changed, "native"));
    }

    [Fact]
    public void RemovingDifferentNativeReceiverPositionsCannotCreateDuplicateAddressMembers()
    {
        InteropModule original = Module();
        StableId integer = new("int"), pointer = new("pointer");
        InteropFunction first = new(new("first"), "first", integer,
            [new(new("first-self"), "self", pointer), new(new("first-value"), "value", integer)], "C");
        InteropFunction last = new(new("last"), "last", integer,
            [new(new("last-value"), "value", integer), new(new("last-self"), "self", pointer)], "C");
        InteropModule module = new(original.libraryId, original.targetId, original.pointerSize, original.types, [first, last], []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        ManagedApiModel model = new DefaultManagedProjection().Project(module, new("Generated.Addresses", "AddressApi")
        {
            pointerCarriers = [new("pointer", "NodeAddress")],
            pointerFunctions = [new("first") { name = "Read", receiver = "self", parameters = [new("self", "NodeAddress")] },
                new("last") { name = "Read", receiver = "self", parameters = [new("self", "NodeAddress")] }]
        }, CancellationToken.None);
        NeutralBackendTests.MemorySink sink = new();
        var failure = Assert.Throws<InvalidOperationException>(() => new CSharpTestPlanner().Plan(model, new("native", sink)));
        Assert.Contains("NodeAddress", failure.Message, StringComparison.Ordinal);
        Assert.Contains("overload", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(sink.sources);
    }

    private static ManagedApiModel Model(
        bool sourceRuntime,
        InteropModule? module = null,
        string? documentation = null
    )
        => new DefaultManagedProjection().Project(module ?? Module(), new("Generated.Addresses", "AddressApi")
        {
            style = sourceRuntime ? ManagedApiStyle.Static : ManagedApiStyle.Instance,
            nestGeneratedTypesInApi = sourceRuntime,
            pointerCarriers = [new("pointer", "NodeAddress") { documentation = documentation }, new("const-pointer", "ConstNodeAddress") { readOnly = true },
                new("pointer-pointer", "NodeListAddress") { pointerDepth = 2 }, new("opaque-pointer", "OpaqueAddress")],
            pointerFunctions = [new("read") { name = "ReadAddress", parameters = [new("self", "NodeAddress")], receiver = "self" }, new("get") { name = "GetAddress", returns = "NodeListAddress" }]
        }, CancellationToken.None);

    private static InteropModule Module()
    {
        InteropType integer = new(new("int"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropType record = new(new("record"), "Node", NativeTypeCategory.Record, 24, 8)
        {
            recordKind = "Struct",
            fields = [new(new("value"), "value", integer.id, 0, 0, null), new(new("values"), "values", new("array"), 4, 32, null),
                new(new("next"), "next", new("pointer"), 16, 128, null)]
        };
        InteropType pointer = new(new("pointer"), "Node*", NativeTypeCategory.Pointer, 8, 8) { elementType = record.id };
        InteropType constant = new(new("constant"), "const Node", NativeTypeCategory.Qualified, 24, 8) { elementType = record.id, qualifier = "Const" };
        InteropType constPointer = pointer with { id = new("const-pointer"), nativeName = "const Node*", elementType = constant.id };
        InteropType opaque = new(new("opaque"), "Opaque", NativeTypeCategory.Record, null, null) { recordKind = "Struct" };
        return new("addresses", "windows-x64-msvc", 8,
            [integer, record, pointer, constant, constPointer, opaque, pointer with { id = new("opaque-pointer"), nativeName = "Opaque*", elementType = opaque.id },
                pointer with { id = new("pointer-pointer"), nativeName = "Node**", elementType = pointer.id },
                pointer with { id = new("const-pointer-pointer"), nativeName = "const Node**", elementType = constPointer.id },
                new(new("pointer-alias"), "Alias", NativeTypeCategory.Alias, 8, 8) { elementType = pointer.id },
                new(new("array"), "int[2]", NativeTypeCategory.Array, 8, 4) { elementType = integer.id, arrayLength = 2 }],
            [new(new("read"), "read_node", integer.id, [new(new("self"), "self", pointer.id)], "C"),
                new(new("get"), "get_nodes", new("pointer-pointer"), [], "C")], [])
        { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
    }
}
