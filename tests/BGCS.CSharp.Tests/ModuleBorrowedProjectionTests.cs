using System;
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

public sealed class ModuleBorrowedProjectionTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable)]
    [InlineData(BindingImportMode.DllImport)]
    [InlineData(BindingImportMode.LibraryImport)]
    public async Task BorrowedObjectRetainsOnlyItsModuleAndProtectsItsExactLifetimeAfterProcessing(BindingImportMode mode)
    {
        ManagedApiModel model = Project(Module());
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native", sink) { importMode = mode };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        Assert.Empty(plan.directOwnedFactories);
        Assert.Equal("WidgetView", Assert.Single(plan.directBorrowedFactories).objectName);
        CSharpObjectPlan wrapper = Assert.Single(plan.objects);
        Assert.Equal(ManagedReceiverKind.ModuleBorrowed, wrapper.kind);
        Assert.Single(wrapper.directMethods);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        string objects = sink.sources.Single(source => source.Key.EndsWith("Objects.g.cs", StringComparison.Ordinal)).Value;
        Assert.Contains("ModuleBorrowedNativeHandle m_handle", objects, StringComparison.Ordinal);
        Assert.DoesNotContain("releaseDomain", objects, StringComparison.Ordinal);
        Assert.DoesNotContain("AsBorrowed()", objects, StringComparison.Ordinal);
        foreach (string mutation in new[] { "lease", "identity", "dispose", "invoke" })
        {
            int changes = 0;
            var altered = sink.sources.Select(source =>
            {
                string changed = mutation switch
                {
                    "lease" => source.Value.Replace("AcquireResource()", "AcquireCleanupCall()", StringComparison.Ordinal),
                    "identity" => source.Value.Replace("ReferenceEquals(m_context, context)", "ReferenceEquals(context, context)", StringComparison.Ordinal),
                    "dispose" => source.Value.Replace("m_handle.Dispose()", "m_context.Dispose()", StringComparison.Ordinal),
                    _ => source.Value.Replace("m_handle.Invoke(", "m_context.Invoke(", StringComparison.Ordinal)
                };
                if (changed != source.Value) changes++;
                return (source.Key, changed);
            }).ToArray();
            Assert.True(changes > 0, mutation);
            Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan, altered, "native"));
        }
    }

    [Theory]
    [InlineData("lifetime")]
    [InlineData("ownership-evidence")]
    [InlineData("lifetime-evidence")]
    [InlineData("release")]
    [InlineData("allocator")]
    [InlineData("owner")]
    [InlineData("input")]
    public async Task MissingOrConflictingModuleValidityKeepsOnlyTheRawGetter(string fault)
    {
        InteropModule original = Module();
        InteropFunction getter = original.functions[0];
        OwnershipContract borrowing = getter.returnOwnership!;
        borrowing = fault switch
        {
            "lifetime" => borrowing with { lifetime = borrowing.lifetime with { kind = NativeLifetimeKind.Owner } },
            "ownership-evidence" => borrowing with { evidence = new(EvidenceLevel.Inferred, "guess") },
            "lifetime-evidence" => borrowing with { lifetime = borrowing.lifetime with { evidence = new(EvidenceLevel.Unknown, "unknown") } },
            "release" => borrowing with { releaseFunction = original.functions[1].id },
            "allocator" => borrowing with { allocator = new("a") },
            "owner" => borrowing with { lifetime = borrowing.lifetime with { owner = getter.id } },
            _ => borrowing
        };
        getter = getter with { returnOwnership = borrowing };
        if (fault == "input") getter = getter with { parameters = [new(new("unproven"), "unproven", getter.returnType)] };
        InteropModule interop = new(original.libraryId, original.targetId, original.pointerSize, original.types,
            original.functions.SetItem(0, getter), [])
        { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        ManagedApiModel model = Project(interop);
        NeutralBackendTests.MemorySink sink = new();
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, new("native", sink));
        Assert.Empty(plan.directBorrowedFactories);
        Assert.Empty(plan.objects);
        Assert.Contains(plan.ownedProjectionOmissions, omission => omission.nativeExport == getter.id);
        await new CSharpTestBackend().EmitAsync(model, new("native", sink), CancellationToken.None);
        Assert.Contains("Widget* GetStatic", string.Join("\n", sink.sources.Values), StringComparison.Ordinal);
        Assert.DoesNotContain(sink.sources.Keys, path => path.EndsWith("Objects.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void BorrowedGettersCannotSupplyOwnedMembersOrAnInventedReleaseDomain()
    {
        ManagedProjectionRequest owned = Request() with
        { memberFunctions = [Request().memberFunctions[0] with { kind = ManagedReceiverKind.Owned }] };
        ManagedApiModel model = new DefaultManagedProjection().Project(Module(), owned, CancellationToken.None);
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, new("native", new NeutralBackendTests.MemorySink()));
        Assert.Empty(plan.objects);
        Assert.Null(Assert.Single(plan.directBorrowedFactories).objectName);
        Assert.Empty(plan.directOwnedFactories);
        Assert.Throws<ArgumentException>(() => new DefaultManagedProjection().Project(Module(), Request() with
        { memberFunctions = [Request().memberFunctions[0] with { releaseDomain = "invented" }] }, CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AModuleLeaseCannotRemoveNativePointeeConstness(bool readOnlyMember)
    {
        InteropModule baseline = Module();
        InteropType qualified = new(new("const-widget"), "const Widget", NativeTypeCategory.Qualified, 4, 4)
        { qualifier = "Const", elementType = new("widget") };
        InteropType pointer = new(new("const-pointer"), "const Widget*", NativeTypeCategory.Pointer, 8, 8) { elementType = qualified.id };
        var functions = baseline.functions.SetItem(0, baseline.functions[0] with { returnType = pointer.id });
        if (readOnlyMember)
            functions = functions.SetItem(1, functions[1] with { parameters = [functions[1].parameters[0] with { type = pointer.id }] });
        InteropModule interop = new(baseline.libraryId, baseline.targetId, baseline.pointerSize,
            baseline.types.AddRange(ImmutableArray.Create(qualified, pointer)), functions, [])
        { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(Project(interop), new("native", new NeutralBackendTests.MemorySink()));
        Assert.Single(plan.directBorrowedFactories);
        if (readOnlyMember) Assert.Single(Assert.Single(plan.objects).directMethods);
        else
        {
            Assert.Empty(Assert.Single(plan.objects).directMethods);
            Assert.Contains(plan.ownedProjectionOmissions, omission => omission.reason.Contains("pointee qualifiers", StringComparison.Ordinal));
        }
    }

    private static ManagedApiModel Project(InteropModule module) => new DefaultManagedProjection().Project(module, Request(), CancellationToken.None);
    private static ManagedProjectionRequest Request() => new("Generated.ModuleBorrowed", "Api")
    {
        names = ImmutableDictionary<string, string>.Empty.Add("get_static", "GetStatic").Add("read", "Read"),
        memberFunctions = [new("read", "Widget", "self") { objectName = "WidgetView", kind = ManagedReceiverKind.ModuleBorrowed }]
    };

    private static InteropModule Module()
    {
        Evidence evidence = new(EvidenceLevel.UserAsserted, "fixture:address-valid-through-actual-module-unload");
        InteropType integer = new(new("int"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropType widget = new(new("widget"), "Widget", NativeTypeCategory.Record, 4, 4)
        { declarationName = "Widget", fields = [new(new("value"), "value", integer.id, 0, 0, null)] };
        InteropType pointer = new(new("pointer"), "Widget*", NativeTypeCategory.Pointer, 8, 8) { elementType = widget.id };
        InteropFunction get = new(new("get_static"), "get_static", pointer.id, [], "C")
        { returnOwnership = new(NativeOwnershipKind.Borrowed, new(NativeLifetimeKind.Module, null, evidence), evidence) };
        InteropFunction read = new(new("read"), "read", integer.id,
            [new(new("self"), "self", pointer.id)
            { ownership = new(NativeOwnershipKind.Borrowed, new(NativeLifetimeKind.Call, null, evidence), evidence) { retainsPointer = false } }], "C");
        return new("module-borrowed", "windows-x64-msvc", 8, [integer, widget, pointer], [get, read], []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
    }
}
