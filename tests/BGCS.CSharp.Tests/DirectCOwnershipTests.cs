using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Analysis.Safety;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Managed;
using BGCS.Contracts.Native;
using BGCS.Contracts.Requests;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using BGCS.CSharp.Validation;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class DirectCOwnershipTests
{
    [Theory]
    [InlineData("missing-release")]
    [InlineData("unknown-evidence")]
    [InlineData("unknown-thread")]
    [InlineData("wrong-release-return")]
    [InlineData("wrong-release-pointer")]
    [InlineData("release-protocol")]
    [InlineData("scalar-return")]
    [InlineData("unproven-input")]
    public async Task UnprovenFactoryOmitsConvenienceAndPreservesExactRawSignature(string condition)
    {
        InteropModule module = Module(condition);
        var model = new DefaultManagedProjection().Project(module, new("Generated.COwnedUnit", "Api"), CancellationToken.None);
        Sink sink = new();
        EmissionRequest request = new("fixture", sink);
        CSharpEmissionPlan plan = new CSharpEmissionPlanner().Plan(model, request);
        Assert.Empty(plan.directOwnedFactories);
        CSharpOwnedProjectionOmissionPlan omission = Assert.Single(plan.ownedProjectionOmissions);
        Assert.Equal(new StableId("make"), omission.nativeExport);
        Assert.Contains("direct owned factory", omission.reason, StringComparison.OrdinalIgnoreCase);
        await new CSharpBackend().EmitAsync(model, request, CancellationToken.None);
        new EmittedInteropContractReader().Verify(plan, sink.contents.Select(pair => (pair.Key, pair.Value)), "fixture");
        Assert.Contains(plan.methods, method => method.nativeExport == new StableId("make"));
        Assert.DoesNotContain(plan.sharedContext?.methods ?? [], method => method.nativeExport == new StableId("make"));
    }

    [Theory]
    [InlineData("retained")]
    [InlineData("unknown")]
    [InlineData("owner-lifetime")]
    [InlineData("missing")]
    public void MemberPresentationDoesNotInventBorrowLifetime(string condition)
    {
        InteropModule module = Module(receiverCondition: condition);
        var model = new DefaultManagedProjection().Project(module, Request(), CancellationToken.None);
        CSharpEmissionPlan plan = new CSharpEmissionPlanner().Plan(model, new("fixture", new Sink()));
        Assert.Single(plan.directOwnedFactories);
        Assert.Empty(Assert.Single(plan.objects).directMethods);
        CSharpOwnedProjectionOmissionPlan omission = Assert.Single(plan.ownedProjectionOmissions);
        Assert.Equal(new StableId("self"), omission.parameter);
        Assert.Contains("nonretaining borrowed Call", omission.reason);
        Assert.All(module.functions, function => Assert.Null(function.sourceOwnerType));
    }

    [Fact]
    public async Task SupportedOwnedFactoryWithoutMemberPresentationReturnsGenericOwnedHandle()
    {
        InteropModule module = Module();
        var model = new DefaultManagedProjection().Project(module, new("Generated.COwnedUnit", "Api"), CancellationToken.None);
        Sink sink = new();
        EmissionRequest request = new("fixture", sink);
        CSharpEmissionPlan plan = new CSharpEmissionPlanner().Plan(model, request);
        Assert.Null(Assert.Single(plan.directOwnedFactories).ownership.objectName);
        Assert.Empty(plan.objects);
        Assert.Empty(plan.ownedProjectionOmissions);
        await new CSharpBackend().EmitAsync(model, request, CancellationToken.None);
        new EmittedInteropContractReader().Verify(plan, sink.contents.Select(pair => (pair.Key, pair.Value)), "fixture");
        Assert.Contains(sink.contents.Values, source => source.Contains("public global::BGCS.Runtime.OwnedNativeHandle Make()", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FactoryCleanupAndReceiverContextDomainAndLeaseRemainProtectedAfterProcessing()
    {
        InteropModule module = Module();
        var model = new DefaultManagedProjection().Project(module, Request(), CancellationToken.None);
        Sink sink = new();
        EmissionRequest request = new("fixture", sink);
        CSharpEmissionPlan plan = new CSharpEmissionPlanner().Plan(model, request);
        Assert.Equal("WidgetObject", Assert.Single(plan.directOwnedFactories).ownership.objectName);
        Assert.Equal("configured-release-domain:a", Assert.Single(Assert.Single(plan.objects).directMethods).requiredReleaseDomain);
        await new CSharpBackend().EmitAsync(model, request, CancellationToken.None);
        var sources = sink.contents.Select(pair => (Path: pair.Key, Text: pair.Value)).ToArray();
        new EmittedInteropContractReader().Verify(plan, sources, "fixture");
        foreach (string target in new[] { "__BgcsBelongsTo(this)", "m_handle.Invoke(", "__BgcsReleaseDirect0", "m_handle.releaseDomain" })
        {
            Assert.Contains(sources, source => source.Text.Contains(target, StringComparison.Ordinal));
            var changed = sources.Select(source => (source.Path, source.Text.Replace(target, target switch
            {
                "__BgcsBelongsTo(this)" => "__BgcsBelongsTo(null!)",
                "m_handle.Invoke(" => "m_handle.UnsafeInvoke(",
                "m_handle.releaseDomain" => "\"configured-release-domain:a\"",
                _ => "__BgcsReleaseChanged0"
            }, StringComparison.Ordinal))).ToArray();
            Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan, changed, "fixture"));
        }
    }

    [Fact]
    public void MismatchedNativeReceiverIdentityIsRejectedBeforeEmission()
    {
        InteropModule module = Module();
        Assert.Throws<ArgumentException>(() => new DefaultManagedProjection().Project(module,
            new("Generated.COwnedUnit", "Api") { memberFunctions = [new("get", "Other", "self")] }, CancellationToken.None));
    }

    [Theory]
    [InlineData("Const", null, false)]
    [InlineData(null, "Const", true)]
    [InlineData("Const", "Const", true)]
    [InlineData("Volatile", null, false)]
    [InlineData(null, "Volatile", true)]
    [InlineData("Unknown", "Unknown", false)]
    public void OwnedPointerQualifiersCannotBeDroppedByTypedReceiverPresentation(string? ownedQualifier, string? receiverQualifier, bool allowed)
    {
        InteropModule baseline = Module();
        var types = baseline.types.ToBuilder();
        StableId Pointer(string prefix, string? qualifier)
        {
            if (qualifier is null) return new("pointer");
            InteropType qualified = new(new(prefix + "-qualified"), qualifier + " Widget", NativeTypeCategory.Qualified, 4, 4)
            { elementType = new("widget"), qualifier = qualifier };
            InteropType pointer = new(new(prefix + "-pointer"), qualifier + " Widget*", NativeTypeCategory.Pointer, 8, 8) { elementType = qualified.id };
            types.Add(qualified); types.Add(pointer); return pointer.id;
        }
        StableId owned = Pointer("owned", ownedQualifier), receiver = Pointer("receiver", receiverQualifier);
        var functions = baseline.functions.Select(function => function.id.value switch
        {
            "make" => function with { returnType = owned },
            "release" => function with { parameters = [function.parameters[0] with { type = owned }] },
            "get" => function with { parameters = [function.parameters[0] with { type = receiver }] },
            _ => function
        }).ToImmutableArray();
        InteropModule module = new(baseline.libraryId, baseline.targetId, baseline.pointerSize, types.ToImmutable(), functions, []) { byteOrder = baseline.byteOrder };
        var model = new DefaultManagedProjection().Project(module, Request(), CancellationToken.None);
        CSharpEmissionPlan plan = new CSharpEmissionPlanner().Plan(model, new("fixture", new Sink()));
        Assert.Single(plan.directOwnedFactories);
        if (allowed) Assert.Single(Assert.Single(plan.objects).directMethods);
        else
        {
            Assert.Empty(Assert.Single(plan.objects).directMethods);
            Assert.Contains("pointee qualifiers", Assert.Single(plan.ownedProjectionOmissions).reason);
        }
    }

    [Fact]
    public void OpaqueAliasesDoNotBecomeInterchangeableThroughTheirSharedVoidPointerStorage()
    {
        var types = Module().types.ToBuilder();
        InteropType address = new(new("address"), "void*", NativeTypeCategory.Pointer, 8, 8) { elementType = new("void") };
        InteropType first = new(new("first"), "FirstHandle", NativeTypeCategory.Alias, 8, 8) { elementType = address.id };
        InteropType second = new(new("second"), "SecondHandle", NativeTypeCategory.Alias, 8, 8) { elementType = address.id };
        types.AddRange(ImmutableArray.Create(address, first, second));
        InteropModule module = new("opaque", "windows-x64-msvc", 8, types.ToImmutable(), [], []);
        Assert.True(NativeReceiverTypeMatcher.Matches(module, first.id, first.id));
        Assert.False(NativeReceiverTypeMatcher.Matches(module, first.id, second.id));
        Assert.False(NativeReceiverTypeMatcher.Matches(module, first.id, address.id));
    }

    internal static ManagedProjectionRequest Request() => new("Generated.COwnedUnit", "Api")
    { memberFunctions = [new("get", "Widget", "self") { objectName = "WidgetObject", releaseDomain = "a" }] };

    internal static InteropModule Module(string? condition = null, string? receiverCondition = null)
    {
        Evidence evidence = new(EvidenceLevel.UserAsserted, "fixture:explicit-c-ownership");
        InteropType empty = new(new("void"), "void", NativeTypeCategory.Primitive, 0, 1) { primitiveKind = "Void" };
        InteropType integer = new(new("integer"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropType widget = new(new("widget"), "Widget", NativeTypeCategory.Record, 4, 4)
        { declarationName = "Widget", recordKind = "Struct", fields = [new(new("field"), "value", integer.id, 0, null, null)] };
        InteropType other = new(new("other"), "Other", NativeTypeCategory.Record, 4, 4)
        { declarationName = "Other", recordKind = "Struct", fields = [new(new("other-field"), "value", integer.id, 0, null, null)] };
        InteropType pointer = new(new("pointer"), "Widget*", NativeTypeCategory.Pointer, 8, 8) { elementType = widget.id };
        InteropType otherPointer = new(new("other-pointer"), "Other*", NativeTypeCategory.Pointer, 8, 8) { elementType = other.id };
        OwnershipContract owned = new(NativeOwnershipKind.Owned, new(NativeLifetimeKind.Owner, new("make"), evidence), evidence)
        { releaseFunction = new("release"), allocator = new("configured-release-domain:a"), releaseThreadDomain = "any" };
        if (condition == "missing-release") owned = owned with { releaseFunction = null };
        if (condition == "unknown-evidence") owned = owned with { evidence = new(EvidenceLevel.Inferred, "fixture:guess") };
        if (condition == "unknown-thread") owned = owned with { releaseThreadDomain = "unknown" };
        InteropFunction make = new(new("make"), "make", condition == "scalar-return" ? integer.id : pointer.id,
            condition == "unproven-input" ? [new(new("input"), "input", pointer.id)] : [], "C") { returnOwnership = owned };
        InteropParameter releaseParameter = new(new("release-self"), "self", condition == "wrong-release-pointer" ? otherPointer.id : pointer.id)
        { ownership = condition == "release-protocol" ? new(NativeOwnershipKind.Transferred, new(NativeLifetimeKind.Call, null, evidence), evidence) : null };
        InteropFunction release = new(new("release"), "release", condition == "wrong-release-return" ? integer.id : empty.id, [releaseParameter], "C");
        OwnershipContract borrowed = new(NativeOwnershipKind.Borrowed,
            new(receiverCondition == "owner-lifetime" ? NativeLifetimeKind.Owner : NativeLifetimeKind.Call, null, evidence), evidence)
        { retainsPointer = receiverCondition == "retained" ? true : receiverCondition == "unknown" ? null : false };
        InteropFunction get = new(new("get"), "get", integer.id,
            [new(new("self"), "self", pointer.id) { ownership = receiverCondition == "missing" ? null : borrowed }], "C");
        return new("c-owned-unit", "windows-x64-msvc", 8, [empty, integer, widget, other, pointer, otherPointer], [make, release, get], [])
        { byteOrder = NativeByteOrder.LittleEndian };
    }

    private sealed class Sink : IArtifactSink
    {
        internal Dictionary<string, string> contents { get; } = new(StringComparer.Ordinal);
        public string WriteText(string relativePath, string content) { contents.Add(relativePath, content); return Path.GetFullPath(relativePath); }
    }
}
