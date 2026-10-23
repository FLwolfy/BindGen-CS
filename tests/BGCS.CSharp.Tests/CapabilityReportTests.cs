using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.CSharp.Validation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using BGCS.Core.Capabilities;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using BGCS.Core.Targets;
using BGCS.CSharp.Projection;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class CapabilityReportTests
{
    [Fact]
    public async Task RemovingAPlannedFriendlyMethodAfterEmissionCannotPublishItsCapabilityClaim()
    {
        var model = NeutralBackendTests.Model();
        NeutralBackendTests.MemorySink sink = new();
        var request = PlanningFixture.Prepare(model, new("native", sink));
        var plan = new BGCS.CSharp.Planning.CSharpEmissionPlanner().Plan(model, request);
        await new CSharpBackend().EmitAsync(model, request, default);
        string path = sink.sources.Keys.Single(path => path.EndsWith(".Methods.g.cs", StringComparison.Ordinal));
        SyntaxNode root = CSharpSyntaxTree.ParseText(sink.sources[path]).GetRoot();
        MethodDeclarationSyntax method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().First();
        string changed = root.RemoveNode(method, SyntaxRemoveOptions.KeepNoTrivia)!.ToFullString();
        Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan,
            sink.sources.Select(pair => (pair.Key, pair.Key == path ? changed : pair.Value)), request.libraryName));
    }
    [Fact]
    public void PlannedFriendlyMethodsAreReportedWithoutConsumerExecutionClaims()
    {
        var model = NeutralBackendTests.Model();
        var plan = new CSharpTestPlanner().Plan(model, new("native", new NeutralBackendTests.MemorySink()));
        Assert.Equal(plan.methods.Length, plan.capabilities.Length);
        Assert.All(plan.capabilities, report =>
        {
            Assert.Equal(DeclarationGenerationStatus.GeneratedFriendly, report.status);
            Assert.Equal(plan.managedRuntime!.runtimeId, report.runtimeId);
            Assert.Equal(ManagedRuntimeProfileIdentity.Capture(plan.managedRuntime), report.runtimeProfileDigest);
            Assert.Empty(report.consumerRequirements);
            Assert.Empty(report.reasons);
        });
    }

    [Fact]
    public void NativeEnumsAndTypedefsReportOnlyTheirActuallyPlannedRawRepresentation()
    {
        var original = NeutralBackendTests.ModelWithStorage();
        InteropType alias = new(new("count-alias"), "Count", NativeTypeCategory.Alias, 4, 4)
        { elementType = new("int"), declarationName = "Count", sourceDeclaration = new("count-declaration") };
        InteropModule module = new(original.interop.libraryId, original.interop.targetId, original.interop.pointerSize,
            original.interop.types.Add(alias), original.interop.functions, [])
        { byteOrder = original.interop.byteOrder };
        var model = new DefaultManagedProjection().Project(module, new("Generated.Capability", "CapabilityApi"), CancellationToken.None);
        var plan = new CSharpTestPlanner().Plan(model, new("native", new NeutralBackendTests.MemorySink()));
        var typedef = Assert.Single(plan.capabilities, report => report.declarationId == alias.id);
        Assert.Equal(DeclarationGenerationStatus.GeneratedRaw, typedef.status);
        Assert.Contains(typedef.reasons, reason => reason.Contains("raw carrier int", StringComparison.Ordinal));
        var enumeration = Assert.Single(plan.capabilities, report => report.declarationId == new BGCS.Core.Common.StableId("flags"));
        Assert.Equal(DeclarationGenerationStatus.GeneratedRaw, enumeration.status);
        Assert.Contains(plan.storage, storage => storage.nativeType == enumeration.declarationId);
    }

    [Fact]
    public void ACorrectRawPointerReturnDoesNotPretendToHaveAnOwnedFriendlyProjection()
    {
        var original = NeutralBackendTests.Model();
        InteropType pointer = new(new("pointer"), "void*", NativeTypeCategory.Pointer, 8, 8) { elementType = new("void") };
        InteropFunction function = new(new("borrow_unknown"), "borrow_unknown", pointer.id, [], "C");
        InteropModule module = new("fixture", original.interop.targetId, 8, original.interop.types.Add(pointer), [function], [])
        { byteOrder = NativeByteOrder.LittleEndian };
        var model = new DefaultManagedProjection().Project(module, new("Generated.Capability", "CapabilityApi"), CancellationToken.None);
        var plan = new CSharpTestPlanner().Plan(model, new("native", new NeutralBackendTests.MemorySink()));
        var report = Assert.Single(plan.capabilities);
        Assert.Equal(function.id, report.declarationId);
        Assert.Equal(DeclarationGenerationStatus.GeneratedRaw, report.status);
        Assert.Contains(report.reasons, reason => reason.Contains("No friendly projection", StringComparison.Ordinal));
        Assert.Single(plan.methods);
    }
}
