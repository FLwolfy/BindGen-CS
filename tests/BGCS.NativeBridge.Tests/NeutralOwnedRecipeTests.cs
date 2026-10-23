using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Core.Artifacts;
using BGCS.Core.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.Core.Targets;
using BGCS.NativeBridge.Emission;
using BGCS.NativeBridge.Planning;
using BGCS.NativeBridge.Recipes;
using Xunit;

namespace BGCS.NativeBridge.Tests;

public sealed class NeutralOwnedRecipeTests
{
    [Fact]
    public void OwnedConversion_ResolvesAnActualTypedReleaseEvenOutsideEntrySelection()
    {
        NativeBridgePlan plan = Plan("release");
        BridgeExport create = plan.exports.Single(export => export.operation.declaration == new StableId("make"));
        BridgeExport release = plan.exports.Single(export => export.id == create.resultRelease);
        Assert.Equal(new StableId("release"), release.operation.declaration);
        Assert.Equal(BridgeOperationKind.FreeCall, release.operation.kind);
        Assert.Equal(create.resultType, Assert.Single(release.parameters).type);
        Assert.Equal(new StableId("native-recipe-allocator:tokens"), create.resultOwnership!.allocatorDomain);
        Assert.Equal("any", create.resultOwnership.releaseThreadDomain);
        Assert.Equal(EvidenceLevel.UserAsserted, create.resultOwnership.evidence.level);
        Sink sink = new();
        new TypedBridgeEmitter().Emit(plan, sink, "fixture");
        Assert.Contains("::Demo::Release(reinterpret_cast<unsigned long long *>(arg0))", sink.files["src/fixture.cpp"]);
        Assert.Contains("new uint64_t(bgcs_native_result.value)", sink.files["src/fixture.cpp"]);
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("mismatched-pointer")]
    [InlineData("status-result")]
    public void UnresolvableOrIncompatibleRelease_DoesNotPublishAnOwnedConversion(string fault)
    {
        Sink sink = new();
        Assert.Throws<InvalidOperationException>(() => new TypedBridgeEmitter().Emit(Plan(fault), sink, "fixture"));
        Assert.Empty(sink.files);
    }

    [Fact]
    public void FiniteVariadicOwnedConversionKeepsTheSameExactReleaseAndAllocatorDomain()
    {
        NativeBridgePlan plan = Plan("release", finite: true);
        BridgeExport create = Assert.Single(plan.exports, export => export.operation.kind == BridgeOperationKind.FiniteVariadicCall);
        BridgeExport release = Assert.Single(plan.exports, export => export.id == create.resultRelease);
        Assert.Equal(new StableId("make"), create.operation.declaration);
        Assert.True(plan.source.GetDeclaration(create.operation.declaration!.Value).isVariadic);
        Assert.Equal(new StableId("release"), release.operation.declaration);
        Assert.Equal(create.resultType, Assert.Single(release.parameters).type);
        Assert.Equal(new StableId("native-recipe-allocator:tokens"), create.resultOwnership!.allocatorDomain);
        Assert.Equal("any", create.resultOwnership.releaseThreadDomain);
        Sink sink = new();
        new TypedBridgeEmitter().Emit(plan, sink, "fixture");
        Assert.Contains("::BGCSGeneratedVariadics::MakeFinite()", sink.files["src/fixture.cpp"]);
        Assert.Contains("new uint64_t(bgcs_native_result.value)", sink.files["src/fixture.cpp"]);
    }

    [Fact]
    public void OwnedRecipe_RejectsScalarConstPointerAndInventedVerifiedEvidence()
    {
        TypeLoweringRecipe recipe = Recipe();
        Assert.Throws<NotSupportedException>(() => new LoweringRecipeCompiler().Compile(recipe with { cAbiType = "uint64_t" }));
        Assert.Throws<NotSupportedException>(() => new LoweringRecipeCompiler().Compile(recipe with { cAbiType = "const uint64_t*" }));
        Assert.Throws<ArgumentException>(() => new LoweringRecipeCompiler().Compile(recipe with
        { ownedResultRelease = recipe.ownedResultRelease! with { evidence = new(EvidenceLevel.Verified, "asserted") } }));
    }

    private static TypeLoweringRecipe Recipe() => new()
    {
        id = "token", typePattern = "Demo::Token", cAbiType = "uint64_t*", resultExpression = "new uint64_t({value}.value)",
        ownedResultRelease = new("Demo::Release", "tokens") { releaseThreadDomain = "any" }
    };

    private static NativeBridgePlan Plan(string fault, bool finite = false)
    {
        SourceLocation location = new("fixture.hpp", 1, 1);
        NativeType scalar = new(new("uint64"), NativeTypeCategory.Primitive, "unsigned long long", 8, 8) { primitiveKind = "UnsignedLongLong", isSigned = false };
        NativeType wrong = new(new("int32"), NativeTypeCategory.Primitive, "int", 4, 4) { primitiveKind = "Int", isSigned = true };
        NativeType pointer = new(new("uint64-pointer"), NativeTypeCategory.Pointer, "unsigned long long*", 8, 8) { elementType = scalar.id };
        NativeType wrongPointer = new(new("int32-pointer"), NativeTypeCategory.Pointer, "int*", 8, 8) { elementType = wrong.id };
        NativeType none = new(new("void"), NativeTypeCategory.Primitive, "void", 0, null) { primitiveKind = "Void" };
        NativeType token = new(new("token"), NativeTypeCategory.Record, "Demo::Token", 8, 8) { declaration = new("token-declaration") };
        NativeDeclaration record = new(new("token-declaration"), "Class", "Token", "Demo::Token", token.id, location) { isCompleteDefinition = true };
        NativeDeclaration make = new(new("make"), "Function", "Make", "Demo::Make", null, location) { returnType = token.id, isVariadic = finite };
        NativeDeclaration release = new(new("release"), "Function", "Release", fault == "absent" ? "Demo::Other" : "Demo::Release", null, location)
        { returnType = fault == "status-result" ? wrong.id : none.id, children = [new("value")] };
        NativeDeclaration value = new(new("value"), "Parameter", "value", "value", fault == "mismatched-pointer" ? wrongPointer.id : pointer.id, location) { parent = release.id };
        NativeModule module = new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8, [scalar, wrong, pointer, wrongPointer, none, token], [record, make, release, value], []) { language = SourceLanguage.Cpp };
        NativeTargetDescriptor target = new(new(module.targetId), "windows", "x64", "msvc", module.triple, new());
        NativeVariadicAdapterPlan? adapter = finite ? new NativeVariadicAdapterPlanner().Plan(module,
            [new() { function = "Demo::Make", name = "MakeFinite", parameters = [] }]) : null;
        NativeBridgePlan result = new NativeBridgePlanner(new LoweringRegistry([(new global::BGCS.Core.Plugins.ExtensionDescriptor("fixture.recipe", global::BGCS.Core.Plugins.ExtensionPort.NativeTypeLowering), new LoweringRecipeCompiler().Compile(Recipe()))])).Plan(
            new(module, target, "fixture", ["fixture.hpp"]), [make.id], variadicAdapters: adapter?.adapters ?? []);
        return result with { postamble = adapter?.source.text ?? string.Empty };
    }

    private sealed class Sink : IArtifactSink
    {
        internal Dictionary<string, string> files { get; } = [];
        public string WriteText(string relativePath, string content) { files.Add(relativePath, content); return relativePath; }
    }
}
