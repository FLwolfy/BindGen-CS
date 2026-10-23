using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class OwnedProjectionTests
{
    [Theory]
    [InlineData("unknown-thread")]
    [InlineData("unknown-lifetime")]
    [InlineData("inferred")]
    [InlineData("wrong-owner")]
    [InlineData("unknown-domain")]
    [InlineData("wrong-release-type")]
    [InlineData("unknown-error-thread")]
    public void IncompleteOwnershipProof_OmitsAutomaticCleanupButPreservesTheRawAbi(string fault)
    {
        InteropModule valid = Module();
        var projection = new DefaultManagedProjection();
        CSharpEmissionPlan verified = new CSharpTestPlanner().Plan(projection.Project(valid, new("Generated.Owned", "WidgetApi"), CancellationToken.None), new("unused", new NeutralBackendTests.MemorySink()));
        Assert.Single(verified.objects);
        Assert.Single(verified.statusMethods, method => method.ownedResult is not null);
        InteropFunction create = valid.functions[0];
        OwnershipContract ownership = create.parameters[1].ownership!;
        OwnershipContract revised = fault switch
        {
            "unknown-thread" => ownership with { releaseThreadDomain = null },
            "unknown-lifetime" => ownership with { lifetime = ownership.lifetime with { kind = NativeLifetimeKind.Unknown } },
            "inferred" => ownership with { evidence = new(EvidenceLevel.Inferred, "naming-guess") },
            "wrong-owner" => ownership with { lifetime = ownership.lifetime with { owner = new StableId("different-result") } },
            "unknown-domain" => ownership with { allocator = null },
            _ => ownership
        };
        var functions = valid.functions.SetItem(0, create with { parameters = create.parameters.SetItem(1, create.parameters[1] with { ownership = revised }) });
        if (fault == "wrong-release-type")
        {
            InteropFunction release = functions[1];
            functions = functions.SetItem(1, release with { parameters = release.parameters.SetItem(0, release.parameters[0] with { type = new("error-pointer") }) });
        }
        if (fault == "unknown-error-thread")
        {
            InteropFunction release = functions[1];
            OwnershipContract error = release.error!.errorOwnership! with { releaseThreadDomain = null };
            functions = functions.SetItem(1, release with { error = release.error with { errorOwnership = error }, parameters = release.parameters.SetItem(1, release.parameters[1] with { ownership = error }) });
        }
        InteropModule incomplete = new(valid.libraryId, valid.targetId, valid.pointerSize, valid.types, functions, []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        CSharpEmissionPlan restricted = new CSharpTestPlanner().Plan(projection.Project(incomplete, new("Generated.Owned", "WidgetApi"), CancellationToken.None), new("unused", new NeutralBackendTests.MemorySink()));
        Assert.Empty(restricted.objects);
        Assert.DoesNotContain(restricted.statusMethods, method => method.ownedResult is not null);
        Assert.Equal(verified.methods.Select(method => method.nativeExport), restricted.methods.Select(method => method.nativeExport));
    }

    internal static InteropModule Module()
    {
        Evidence evidence = new(EvidenceLevel.UserAsserted, "test:explicit-resource-protocol");
        StableId native = new("cpp:Widget"), allocator = new("widget-allocation-domain");
        InteropType integer = new(new("int"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropType empty = new(new("void"), "void", NativeTypeCategory.Primitive, 0, 1) { primitiveKind = "Void" };
        InteropType handle = new(new("widget"), "widget", NativeTypeCategory.Record, null, null) { declarationName = "Widget", sourceNativeType = native };
        InteropType pointer = new(new("widget-pointer"), "widget*", NativeTypeCategory.Pointer, 8, 8) { elementType = handle.id };
        InteropType output = new(new("widget-output"), "widget**", NativeTypeCategory.Pointer, 8, 8) { elementType = pointer.id };
        InteropType errorType = new(new("error"), "error", NativeTypeCategory.Record, null, null) { declarationName = "Error" };
        InteropType errorPointer = new(new("error-pointer"), "error*", NativeTypeCategory.Pointer, 8, 8) { elementType = errorType.id };
        InteropType errorOutput = new(new("error-output"), "error**", NativeTypeCategory.Pointer, 8, 8) { elementType = errorPointer.id };
        StableId cleanup = new("destroy"), errorCleanup = new("release-error");
        InteropParameter error(StableId id) => new(id, "error", errorOutput.id)
        { ownership = new(NativeOwnershipKind.Owned, new(NativeLifetimeKind.Owner, id, evidence), evidence) { releaseFunction = errorCleanup, allocator = allocator, releaseThreadDomain = "any" } };
        InteropParameter result = new(new("created-result"), "result", output.id)
        { ownership = new(NativeOwnershipKind.Owned, new(NativeLifetimeKind.Owner, new("created-result"), evidence), evidence) { releaseFunction = cleanup, allocator = allocator, releaseThreadDomain = "any" } };
        InteropParameter createError = error(new("create-error")), destroyError = error(new("destroy-error"));
        InteropFunction create = new(new("create"), "create_widget", integer.id, [new(new("value"), "value", integer.id), result, createError], "C")
        { sourceOwnerType = native, error = new("0", evidence) { valueParameter = result.id, errorParameter = createError.id, errorOwnership = createError.ownership } };
        InteropFunction destroy = new(cleanup, "destroy_widget", integer.id, [new(new("self"), "self", pointer.id), destroyError], "C")
        { sourceOwnerType = native, error = new("0", evidence) { errorParameter = destroyError.id, errorOwnership = destroyError.ownership } };
        InteropFunction releaseError = new(errorCleanup, "release_error", empty.id, [new(new("error-value"), "error", errorPointer.id)], "C");
        return new("widget", "windows-x64-msvc", 8, [integer, empty, handle, pointer, output, errorType, errorPointer, errorOutput], [create, destroy, releaseError], []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
    }
}
