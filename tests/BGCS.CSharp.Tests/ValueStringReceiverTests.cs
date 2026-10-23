using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class ValueStringReceiverTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task OwnedMemberRetainsItsReceiverAndFreshTextWhileComputingHiddenInputCounts(BindingImportMode mode, bool sourceRuntime)
    {
        ManagedApiModel model = ValueStringResultTests.Project(Module());
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("value_strings", sink)
        { importMode = mode, includeRuntimeSource = sourceRuntime, runtimeNamespace = sourceRuntime ? "Generated.ValueStrings.Runtime" : "BGCS.Runtime" };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        CSharpValueStringResultPlan result = Assert.Single(plan.valueStringResults);
        Assert.Equal("WidgetObject", Assert.IsType<CSharpValueStringReceiverPlan>(result.receiver).objectName);
        Assert.Equal("string", Assert.Single(result.receiver.signature.parameterTypes));
        Assert.Equal("text", Assert.Single(result.receiver.signature.parameterNames));
        Assert.EndsWith(".WidgetObject", result.signature.parameterTypes[0], StringComparison.Ordinal);
        Assert.Contains(plan.statusMethods, operation => operation.ownedResult is not null && operation.instanceIndex >= 0);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        const string consumer = """
            namespace Generated.ValueStrings;
            public static class Consumer {
                public static string Read(Api api) {
                    using var widget = api.CreateWidget();
                    widget.SetText("A\0😀B");
                    using var copy = widget.Clone("other");
                    return copy.Make("prefix") + api.Make(copy, "context");
                }
            }
            """;
        await Compile(sink, consumer, mode, sourceRuntime);
        new EmittedInteropContractReader().Verify(plan, sink.sources.Select(pair => (pair.Key, pair.Value)), request.libraryName);
        string owned = Assert.Single(sink.sources, pair => pair.Value.Contains("private string __BgcsValueTextReceiver", StringComparison.Ordinal)).Value;
        MethodDeclarationSyntax helper = CSharpSyntaxTree.ParseText(owned).GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText.StartsWith("__BgcsValueTextReceiver", StringComparison.Ordinal));
        Assert.Equal(2, helper.ParameterList.Parameters.Count);
        Assert.Contains("EncodeCounted(", helper.ToFullString());
        Assert.Contains(".encodedCodeUnitCount", helper.ToFullString());
        Assert.Contains("RecordUnresolvedCleanup(", helper.ToFullString());
        foreach (string token in new[] { "this.m_handle.Invoke(", "this.m_context.__BgcsValueTextReceiver", ".encodedCodeUnitCount", "RecordUnresolvedCleanup(", ".AcquireResource()", ".__BgcsBelongsTo(this)" })
        {
            var changed = sink.sources.Select(pair => (pair.Key, pair.Value.Replace(token, "Changed" + token, StringComparison.Ordinal))).ToArray();
            Assert.Contains(changed, pair => pair.Item2 != sink.sources[pair.Key]);
            Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan, changed, request.libraryName));
        }
    }

    [Theory]
    [InlineData("retained")]
    [InlineData("unknown-call")]
    [InlineData("other-owner")]
    [InlineData("const-owned")]
    public void MemberConvenienceNeverInventsAReceiverBorrowOrRemovesOwnedQualifiers(string fault)
    {
        InteropModule module = Module();
        InteropFunction member = module.functions.Single(function => function.id == new StableId("make"));
        if (fault == "other-owner") member = member with { sourceOwnerType = new("different-owner") };
        else if (fault != "const-owned")
        {
            InteropParameter self = member.parameters[0];
            member = member with { parameters = member.parameters.SetItem(0, self with { ownership = self.ownership! with
            { retainsPointer = fault == "retained" ? true : null, lifetime = self.ownership.lifetime with { kind = fault == "unknown-call" ? NativeLifetimeKind.Unknown : NativeLifetimeKind.Call } } }) };
        }
        var types = module.types;
        var functions = module.functions.SetItem(module.functions.IndexOf(module.functions.Single(function => function.id == member.id)), member);
        if (fault == "const-owned")
        {
            InteropType qualified = new(new("qualified-widget"), "const Widget", NativeTypeCategory.Qualified, null, null) { elementType = new("widget"), qualifier = "Const" };
            InteropType pointer = new(new("qualified-widget-pointer"), "const Widget*", NativeTypeCategory.Pointer, 8, 8) { elementType = qualified.id };
            InteropType output = new(new("qualified-widget-output"), "const Widget**", NativeTypeCategory.Pointer, 8, 8) { elementType = pointer.id };
            types = types.AddRange(ImmutableArray.Create(qualified, pointer, output));
            functions = functions.Select(function => function.id.value is "create-widget" or "clone-widget"
                ? function with { parameters = function.parameters.Select(parameter => parameter.name == "result" ? parameter with { type = output.id } : parameter).ToImmutableArray() }
                : function.id.value == "destroy-widget" ? function with { parameters = function.parameters.SetItem(0, function.parameters[0] with { type = pointer.id }) } : function).ToImmutableArray();
        }
        module = new(module.libraryId, module.targetId, module.pointerSize, types, functions, []) { byteOrder = module.byteOrder };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(ValueStringResultTests.Project(module), new("value_strings", new NeutralBackendTests.MemorySink()));
        Assert.DoesNotContain(plan.valueStringResults, result => result.receiver is not null);
        Assert.Contains(plan.methods, method => method.nativeExport == member.id);
    }

    [Fact]
    public void UnknownSelfRetentionWithoutAnOwnedFactoryRemainsOnlyRaw()
    {
        InteropModule original = Module();
        InteropModule module = new(original.libraryId, original.targetId, original.pointerSize, original.types,
            original.functions.Where(function => function.id.value is not ("create-widget" or "clone-widget")).ToImmutableArray(), []) { byteOrder = original.byteOrder };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(ValueStringResultTests.Project(module), new("value_strings", new NeutralBackendTests.MemorySink()));
        Assert.Empty(plan.valueStringResults);
        Assert.Contains(plan.methods, method => method.nativeExport == new StableId("make"));
        Assert.Contains(plan.ownedProjectionOmissions, omission => omission.nativeExport == new StableId("make"));
    }

    internal static async Task Compile(NeutralBackendTests.MemorySink sink, string consumer, BindingImportMode mode, bool sourceRuntime)
    {
        if (mode == BindingImportMode.LibraryImport) { await ValueReceiverTests.CompileWithSdkAsync(sink, consumer, sourceRuntime); return; }
        CSharpCompilation compilation = CSharpCompilation.Create("ReceiverComposition_" + Guid.NewGuid().ToString("N"),
            sink.sources.Select(pair => CSharpSyntaxTree.ParseText(pair.Value, new(LanguageVersion.CSharp14), pair.Key)).Append(CSharpSyntaxTree.ParseText(consumer)),
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Append(typeof(BGCS.Runtime.NativeModuleSession).Assembly.Location)
                .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path)),
            new(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error));
        using MemoryStream image = new();
        var emitted = compilation.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
    }

    internal static InteropModule Module()
    {
        InteropModule original = ValueStringResultTests.Module(NativeTextEncoding.Utf8);
        Evidence evidence = new(EvidenceLevel.UserAsserted, "fixture:owned-widget-and-call-text");
        StableId owner = new("widget-source");
        InteropType widget = new(new("widget"), "Widget", NativeTypeCategory.Record, null, null) { declarationName = "Widget", sourceNativeType = owner };
        InteropType pointer = new(new("widget-pointer"), "Widget*", NativeTypeCategory.Pointer, 8, 8) { elementType = widget.id };
        InteropType output = new(new("widget-output"), "Widget**", NativeTypeCategory.Pointer, 8, 8) { elementType = pointer.id };
        InteropParameter Self(string id) => new(new(id), "self", pointer.id)
        { ownership = new(NativeOwnershipKind.Borrowed, new(NativeLifetimeKind.Call, null, evidence), evidence) };
        ImmutableArray<InteropParameter> Text(string prefix) =>
        [new(new(prefix + "-text"), "text", new("unit-pointer")) { text = new(NativeTextEncoding.Utf8, 1, NativeLengthUnit.CodeUnits,
            NativeParameterDirection.Input, new(NativeLifetimeKind.Call, null, evidence), evidence)
            { zeroTerminated = false, allowsNull = false, retainsPointer = false, lengthParameter = new(prefix + "-length"), lengthDirection = NativeParameterDirection.Input } },
         new(new(prefix + "-length"), "length", new("count"))];
        InteropFunction Status(string id, string name, ImmutableArray<InteropParameter> inputs, bool ownedResult)
        {
            StableId errorId = new(id + "-error"), resultId = new(id + "-result");
            OwnershipContract error = original.functions[0].error!.errorOwnership! with { lifetime = new(NativeLifetimeKind.Owner, errorId, evidence) };
            if (ownedResult) inputs = inputs.Add(new(resultId, "result", output.id)
            { ownership = new(NativeOwnershipKind.Owned, new(NativeLifetimeKind.Owner, resultId, evidence), evidence)
                { releaseFunction = new("destroy-widget"), allocator = new("widget-domain"), releaseThreadDomain = "any" } });
            return new(new(id), name, new("status"), inputs.Add(new(errorId, "error", new("error-output")) { ownership = error }), "C")
            { sourceOwnerType = owner, error = new("0", evidence) { valueParameter = ownedResult ? resultId : null, errorParameter = errorId, errorOwnership = error } };
        }
        InteropFunction make = original.functions[0] with
        { parameters = ImmutableArray.Create(Self("make-self")).AddRange(Text("make")).AddRange(original.functions[0].parameters), sourceOwnerType = owner };
        return new(original.libraryId, original.targetId, original.pointerSize, original.types.AddRange(ImmutableArray.Create(widget, pointer, output)),
            original.functions.SetItem(0, make).AddRange(ImmutableArray.Create(Status("create-widget", "CreateWidget", [], true),
                Status("destroy-widget", "DestroyWidget", [Self("destroy-widget-self") with { ownership = null }], false), Status("set-text", "SetText", ImmutableArray.Create(Self("set-self")).AddRange(Text("set")), false),
                Status("clone-widget", "Clone", ImmutableArray.Create(Self("clone-self")).AddRange(Text("clone")), true))), []) { byteOrder = original.byteOrder };
    }
}
