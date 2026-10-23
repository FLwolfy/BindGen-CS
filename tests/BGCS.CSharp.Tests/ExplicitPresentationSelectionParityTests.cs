using System.Collections.Immutable;
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
using Xunit;

namespace BGCS.CSharp.Tests;

/// <summary>Checks explicit managed-only selections independently of native source selection and required cleanup.</summary>
public sealed class ExplicitPresentationSelectionParityTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, 0)]
    [InlineData(BindingImportMode.FunctionTable, 1)]
    [InlineData(BindingImportMode.FunctionTable, 2)]
    [InlineData(BindingImportMode.DllImport, 0)]
    [InlineData(BindingImportMode.DllImport, 1)]
    [InlineData(BindingImportMode.DllImport, 2)]
    [InlineData(BindingImportMode.LibraryImport, 0)]
    [InlineData(BindingImportMode.LibraryImport, 1)]
    [InlineData(BindingImportMode.LibraryImport, 2)]
    public async Task ExplicitMemberWhitelistCanDisableOrSelectConveniencesWithoutRemovingAnyRawCall(BindingImportMode mode, int selected)
    {
        InteropModule module = Module(valueProtocol: true);
        ImmutableArray<ManagedReceiverRequest> members = selected switch
        {
            0 => [],
            1 => [Member("read")],
            _ => [Member("read"), Member("write")]
        };
        ManagedApiModel original = Project(module, new("Generated.Selection", "SelectionApi"));
        ManagedApiModel selectedModel = Project(module, new("Generated.Selection", "SelectionApi") { memberFunctions = members });
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("selection_native", sink) { importMode = mode };
        CSharpEmissionPlan before = new CSharpTestPlanner().Plan(original, request);
        CSharpEmissionPlan after = new CSharpTestPlanner().Plan(selectedModel, request);
        CSharpValueReceiverPlan[] actual = after.storage.SelectMany(type => type.valueMembers).ToArray();
        Assert.Equal(selected, actual.Length);
        Assert.Equal(selected == 0 ? System.Array.Empty<string>() : selected == 1 ? new[] { "Read" } : new[] { "Read", "Write" },
            actual.Select(member => member.signature.name).OrderBy(name => name, System.StringComparer.Ordinal));
        Assert.Equal(Signatures(before), Signatures(after));
        Assert.Equal(new[] { "read", "write" }, after.methods.Select(method => method.nativeExport.value));
        Assert.Equal(before.storage.Select(type => (type.nativeType, type.name, type.size, type.alignment)),
            after.storage.Select(type => (type.nativeType, type.name, type.size, type.alignment)));
        Assert.Same(module, selectedModel.interop);
        Assert.Empty(after.objects);
        Assert.Empty(after.valueParameterOmissions);
        await new CSharpTestBackend().EmitAsync(selectedModel, request, CancellationToken.None);
        new EmittedInteropContractReader().Verify(after, sink.sources.Select(source => (source.Key, source.Value)), request.libraryName);
    }

    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task NamedNonowningHandleViewsAreOptionalAndNeverReplaceRawNativeCarriers(BindingImportMode mode, bool enabled)
    {
        InteropModule module = Module(valueProtocol: false);
        ManagedProjectionRequest choices = new("Generated.Selection", "SelectionApi")
        {
            pointerCarriers = enabled ? [new("node-pointer", "NodeAddress")] : [],
            pointerFunctions = enabled ? [new("read") { name = "ReadAddress", parameters = [new("self", "NodeAddress")] }] : []
        };
        ManagedApiModel model = Project(module, choices);
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("selection_native", sink) { importMode = mode };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        Assert.Equal(enabled ? 1 : 0, plan.pointerCarriers.Length);
        Assert.Equal(enabled ? 1 : 0, plan.pointerFunctions.Length);
        Assert.Equal(new[] { "read", "write" }, plan.methods.Select(method => method.nativeExport.value));
        Assert.All(plan.methods, method => Assert.EndsWith("*", method.parameterTypes[0], System.StringComparison.Ordinal));
        Assert.DoesNotContain(plan.methods.SelectMany(method => method.parameterTypes), type => type.Contains("NodeAddress", System.StringComparison.Ordinal));
        Assert.Equal(Signatures(new CSharpTestPlanner().Plan(Project(module, new("Generated.Selection", "SelectionApi")), request)), Signatures(plan));
        Assert.Same(module, model.interop);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        new EmittedInteropContractReader().Verify(plan, sink.sources.Select(source => (source.Key, source.Value)), request.libraryName);
        Assert.Equal(enabled, sink.sources.Values.Any(source => source.Contains("struct NodeAddress", System.StringComparison.Ordinal)));
    }

    [Fact]
    public void OmittingNonowningViewsMustNotDisableAnIndependentlyRequiredOwnedReleaseProtocol()
    {
        InteropModule module = OwnedProjectionTests.Module();
        ManagedApiModel model = Project(module, new("Generated.Selection", "SelectionApi")
        { memberFunctions = [], pointerCarriers = [], pointerFunctions = [] });
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, new("selection_native", new NeutralBackendTests.MemorySink()));
        Assert.Single(plan.objects);
        CSharpStatusMethodPlan factory = Assert.Single(plan.statusMethods, method => method.ownedResult is not null);
        Assert.Equal("destroy", plan.methods[factory.ownedResult!.releaseIndex].nativeExport.value);
        Assert.Contains(plan.methods, method => method.nativeExport.value == "release-error");
        Assert.Empty(plan.pointerCarriers);
        Assert.Empty(plan.pointerFunctions);
        Assert.Same(module, model.interop);
    }

    private static ManagedReceiverRequest Member(string function) => new(function, "Node", "self") { kind = ManagedReceiverKind.Value };
    private static ManagedApiModel Project(InteropModule module, ManagedProjectionRequest request)
        => new DefaultManagedProjection().Project(module, request with
        { names = ImmutableDictionary<string, string>.Empty.Add("native_read", "Read").Add("native_write", "Write") }, CancellationToken.None);
    private static string[] Signatures(CSharpEmissionPlan plan) => plan.methods.Select(method => method.nativeExport.value + ":" + method.exportName
        + ":" + method.returnType + "(" + string.Join(",", method.parameterTypes) + ")").ToArray();

    private static InteropModule Module(bool valueProtocol)
    {
        Evidence evidence = new(EvidenceLevel.UserAsserted, "test:explicit-call-only-value-receiver");
        InteropType integer = new(new("int"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropType record = new(new("node"), "Node", NativeTypeCategory.Record, 4, 4)
        { recordKind = "Struct", fields = [new(new("node-value"), "value", integer.id, 0, 0, null)] };
        InteropType pointer = new(new("node-pointer"), "Node*", NativeTypeCategory.Pointer, 8, 8) { elementType = record.id };
        ValueParameterContract? Contract(NativeParameterDirection direction) => valueProtocol
            ? new(direction, new(NativeLifetimeKind.Call, null, evidence), evidence) { allowsNull = false, retainsPointer = false } : null;
        InteropFunction read = new(new("read"), "native_read", integer.id,
            [new(new("read-self"), "self", pointer.id) { singleValue = Contract(NativeParameterDirection.Input) }], "C");
        InteropFunction write = new(new("write"), "native_write", integer.id,
            [new(new("write-self"), "self", pointer.id) { singleValue = Contract(NativeParameterDirection.InputOutput) }, new(new("write-value"), "value", integer.id)], "C");
        return new("selection", "windows-x64-msvc", 8, [integer, record, pointer], [read, write], []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
    }
}
