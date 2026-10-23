using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.Core.Targets;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using BGCS.CSharp.Validation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class PhysicalImportTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable)]
    [InlineData(BindingImportMode.DllImport)]
    [InlineData(BindingImportMode.LibraryImport)]
    public async Task BrowserAddressTransportKeepsTypedAliasesNullAndReturnedCallablesWithExactFinalGuards(BindingImportMode mode)
    {
        ManagedApiModel model = Model();
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("__Internal", sink)
        {
            importMode = mode,
            managedRuntime = Profile(),
            staticExportTable = mode == BindingImportMode.FunctionTable ? new("transport", "transport_get", "transport_revision", "transport_count",
                "transport_identity", "transport_pointer_size", new string('a', 64), ["exchange"]) : null
        };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        CSharpPhysicalImportPlan physical = Assert.Single(plan.physicalImports);
        Assert.Equal("nint", physical.returnType);
        Assert.Equal(new[] { "nint", "void*" }, physical.parameterTypes);
        Assert.Equal(new[] { 0 }, physical.addressParameters);
        Assert.True(physical.addressResult);
        Assert.All(new[] { plan.methods[0].returnType, plan.methods[0].parameterTypes[0] }, type => Assert.StartsWith("delegate* unmanaged[Cdecl]", type, StringComparison.Ordinal));
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        new EmittedInteropContractReader().Verify(plan, sink.sources.Select(item => (item.Key, item.Value)), request.libraryName);
        string key = Assert.Single(sink.sources.Keys, path => mode == BindingImportMode.FunctionTable
            ? path.EndsWith("Api.g.cs", StringComparison.Ordinal) && !path.Contains("Raw", StringComparison.Ordinal)
            : path.Contains("NativeImports", StringComparison.Ordinal));
        string source = sink.sources[key];
        var syntax = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
        CastExpressionSyntax address = Assert.Single(syntax.DescendantNodes().OfType<CastExpressionSyntax>(), cast => cast.Type.ToString() == "nint"
            && cast.Expression is IdentifierNameSyntax argument && argument.Identifier.ValueText == "arg0");
        string zeroed = syntax.ReplaceNode(address, address.WithExpression(SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0)))).ToFullString();
        Reject(key, zeroed);
        CastExpressionSyntax returned = Assert.Single(syntax.DescendantNodes().OfType<CastExpressionSyntax>(), cast => cast.Type is FunctionPointerTypeSyntax && cast.Parent is ReturnStatementSyntax);
        Reject(key, syntax.ReplaceNode(returned, returned.WithExpression(SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0)))).ToFullString());
        string rawKey = Assert.Single(sink.sources.Keys, path => path.Contains("Raw", StringComparison.Ordinal));
        Reject(rawKey, sink.sources[rawKey].Replace("this.m_owner.m_function0", "this.m_owner.m_nativeFunction0", StringComparison.Ordinal)
            .Replace(".NativeImports.Function0", ".NativeImports.PhysicalFunction0", StringComparison.Ordinal));
        Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan with
        { managedRuntime = plan.managedRuntime! with { functionPointerTransport = ManagedFunctionPointerTransport.Typed } },
            sink.sources.Select(item => (item.Key, item.Value)), request.libraryName));
        CSharpPhysicalImportPlan changed = physical with { addressParameters = [] };
        Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan with { physicalImports = [changed] },
            sink.sources.Select(item => (item.Key, item.Value)), request.libraryName));
        await ValueReceiverTests.CompileWithSdkAsync(sink, """
            public static unsafe class Consumer {
                public static void Check(Generated.Transport.TransportApi api) {
                    delegate* unmanaged[Cdecl]<int, int, void*, void> callback = api.Raw.Exchange(null, null);
                    if (callback != null) callback(1, 2, null);
                }
            }
            """, sourceRuntime: false);

        void Reject(
            string path,
            string changedSource
        ) {
            Assert.NotEqual(sink.sources[path], changedSource);
            Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan,
                sink.sources.Select(item => (item.Key, item.Key == path ? changedSource : item.Value)), request.libraryName));
        }
    }

    [Theory]
    [InlineData(BindingImportMode.FunctionTable)]
    [InlineData(BindingImportMode.DllImport)]
    [InlineData(BindingImportMode.LibraryImport)]
    public void DefaultDesktopImportsKeepTheirOriginalTypedCarriers(BindingImportMode mode)
    {
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(Model(), new("native", new NeutralBackendTests.MemorySink()) { importMode = mode });
        Assert.Empty(plan.physicalImports);
        Assert.StartsWith("delegate*", plan.methods[0].returnType, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("runtime")]
    [InlineData("module")]
    [InlineData("width")]
    [InlineData("endian")]
    [InlineData("enum")]
    [InlineData("missing-capability")]
    [InlineData("dynamic-thunks")]
    [InlineData("blocking")]
    public void TableIndexTransportRequiresItsExactRuntimeCapability(string fault)
    {
        ManagedApiModel model = Model();
        EmissionRequest request = PlanningFixture.Prepare(model, new("native", new NeutralBackendTests.MemorySink())
        { importMode = BindingImportMode.DllImport, managedRuntime = Profile() });
        ManagedRuntimeProfile profile = request.managedRuntime!;
        profile = fault switch
        {
            "runtime" => profile with { runtimeId = new ManagedRuntimeId("fixture.runtime") },
            "module" => profile with { moduleMechanism = NativeModuleMechanism.DynamicLibrary },
            "enum" => profile with { functionPointerTransport = (ManagedFunctionPointerTransport)12345 },
            "missing-capability" => profile with { functionPointerTransport = ManagedFunctionPointerTransport.Typed },
            "dynamic-thunks" => profile with { requiresStaticCallbackThunks = false },
            "blocking" => profile with { allowsBlockingDisposal = true },
            _ => profile
        };
        if (fault is "width" or "endian")
            model = model with
            {
                interop = new(model.interop.libraryId, model.interop.targetId, fault == "width" ? 8 : 4,
                model.interop.types, model.interop.functions, [])
                { byteOrder = fault == "endian" ? NativeByteOrder.BigEndian : NativeByteOrder.LittleEndian }
            };
        if (fault is "width" or "endian" or "enum")
            Assert.Throws<NotSupportedException>(() => new CSharpEmissionPlanner().Plan(model, request with { managedRuntime = profile }));
        else Assert.Throws<InvalidOperationException>(() => new CSharpEmissionPlanner().Plan(model, request with { managedRuntime = profile }));
    }

    [Fact]
    public void FunctionAddressTransportNeverPermitsAnUnprovedByValueAggregate()
    {
        ManagedApiModel original = Model();
        InteropType integer = original.interop.types[1];
        InteropType record = new(new("record"), "Record", NativeTypeCategory.Record, 4, 4)
        { declarationName = "Record", recordKind = "Struct", fields = [new(new("record-field"), "value", integer.id, 0, 0, null)] };
        InteropFunction function = original.interop.functions[0];
        function = function with { parameters = function.parameters.Add(new(new("record-input"), "record", record.id)) };
        InteropModule module = new("transport", original.interop.targetId, 4, original.interop.types.Add(record), [function], [])
        { byteOrder = NativeByteOrder.LittleEndian };
        ManagedApiModel model = new DefaultManagedProjection().Project(module, new("Generated.Transport", "TransportApi"), CancellationToken.None);
        NotSupportedException failure = Assert.ThrowsAny<NotSupportedException>(() => new CSharpTestPlanner().Plan(model,
            new("native", new NeutralBackendTests.MemorySink()) { importMode = BindingImportMode.DllImport, managedRuntime = Profile() }));
        Assert.Contains("aggregate", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ManagedRuntimeProfile Profile() => new("browser-static-table-index", new ManagedRuntimeId("fixture.nonblocking-runtime"),
        NativeModuleMechanism.StaticAdapter, [BindingImportMode.FunctionTable, BindingImportMode.DllImport, BindingImportMode.LibraryImport], true, false)
    {
        functionPointerTransport = ManagedFunctionPointerTransport.NativeInteger,
        libraryImportConvention = ManagedImportConventionStrategy.RuntimeDefault,
        defaultCallingConvention = "Cdecl"
    };

    private static ManagedApiModel Model()
    {
        InteropType empty = new(new("void"), "void", NativeTypeCategory.Primitive, 0, 1) { primitiveKind = "Void" };
        InteropType integer = new(new("int"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropType data = new(new("data"), "void*", NativeTypeCategory.Pointer, 4, 4) { elementType = empty.id };
        InteropType signature = new(new("signature"), "void (int, int, void*)", NativeTypeCategory.Function, null, null)
        { returnType = empty.id, parameterTypes = [integer.id, integer.id, data.id], callingConvention = "C" };
        InteropType pointer = new(new("callback"), "void (*)(int, int, void*)", NativeTypeCategory.Pointer, 4, 4) { elementType = signature.id };
        InteropType alias = new(new("alias"), "Completion", NativeTypeCategory.Alias, 4, 4) { elementType = pointer.id };
        InteropType qualified = new(new("qualified"), "Completion const", NativeTypeCategory.Qualified, 4, 4) { elementType = alias.id, qualifier = "Const" };
        InteropFunction exchange = new(new("exchange"), "exchange", alias.id, [new(new("input"), "callback", qualified.id), new(new("state"), "state", data.id)], "C");
        InteropModule module = new("transport", "emscripten-wasm32-emscripten", 4, [empty, integer, data, signature, pointer, alias, qualified], [exchange], [])
        { byteOrder = NativeByteOrder.LittleEndian };
        return new DefaultManagedProjection().Project(module, new("Generated.Transport", "TransportApi"), CancellationToken.None);
    }
}
