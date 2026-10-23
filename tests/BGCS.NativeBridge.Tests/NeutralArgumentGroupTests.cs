using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Analysis.Bridge;
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

public sealed class NeutralArgumentGroupTests
{
    [Fact]
    public void MultiCarrierRecipe_PreservesNativeArityAndExactInputExpressions()
    {
        NativeBridgePlan plan = Plan();
        BridgeExport export = Assert.Single(plan.exports);
        Assert.Equal(3, export.parameters.Length);
        Assert.Equal(2, export.operation.argumentGroups.Length);
        Assert.Equal(new StableId("range-parameter"), export.operation.argumentGroups[0].sourceParameter);
        Assert.Equal(2, export.operation.argumentGroups[0].inputs.Length);
        Assert.Equal(EvidenceLevel.UserAsserted, export.operation.argumentGroups[0].expression!.evidence.level);
        Sink sink = new();
        new TypedBridgeEmitter().Emit(plan, sink, "fixture");
        Assert.Contains("::Demo::Sum((::Demo::Range(arg0_data, arg0_length)), static_cast<int>(arg1))", sink.files["src/fixture.cpp"]);
        Assert.Contains("const int32_t * arg0_data, uint64_t arg0_length, int32_t arg1", sink.files["include/fixture.h"]);
    }

    [Theory]
    [InlineData("missing-carrier")]
    [InlineData("duplicate-carrier")]
    [InlineData("missing-native-parameter")]
    [InlineData("wrong-native-type")]
    [InlineData("wrong-order")]
    [InlineData("wrong-source-identity")]
    [InlineData("missing-expression")]
    [InlineData("unknown-placeholder")]
    public void InvalidGroups_AreRejectedBeforeAnyArtifactIsWritten(string fault)
    {
        NativeBridgePlan plan = Plan();
        BridgeExport export = Assert.Single(plan.exports);
        BridgeArgumentGroup first = export.operation.argumentGroups[0], second = export.operation.argumentGroups[1];
        ImmutableArray<BridgeArgumentGroup> groups = fault switch
        {
            "missing-carrier" => [first with { inputs = [first.inputs[0]] }, second],
            "duplicate-carrier" => [first with { inputs = [first.inputs[0], first.inputs[0] with { name = "other" }] }, second],
            "missing-native-parameter" => [first],
            "wrong-native-type" => [first with { nativeType = new("int") }, second],
            "wrong-order" => [second, first],
            "wrong-source-identity" => [first with { sourceParameter = second.sourceParameter }, second],
            "missing-expression" => [first with { expression = null }, second],
            "unknown-placeholder" => [first with { expression = LoweringRecipeCompiler.Expression("test", "{nativeType}({missing})") }, second],
            _ => throw new ArgumentException(nameof(fault))
        };
        plan = plan with { exports = [export with { operation = export.operation with { argumentGroups = groups } }] };
        Sink sink = new();
        Assert.Throws<InvalidOperationException>(() => new TypedBridgeEmitter().Emit(plan, sink, "fixture"));
        Assert.Empty(sink.files);
    }

    [Fact]
    public void DeclarativeCarrierInputs_RejectUninitializedDuplicateAndUnprovedManagedInputs()
    {
        TypeLoweringRecipe recipe = Recipe();
        Assert.Throws<ArgumentException>(() => new LoweringRecipeCompiler().Compile(recipe with { abiParameters = default }));
        Assert.Throws<ArgumentException>(() => new LoweringRecipeCompiler().Compile(recipe with { abiParameters = [recipe.abiParameters[0], recipe.abiParameters[0]] }));
        Assert.Throws<ArgumentException>(() => new LoweringRecipeCompiler().Compile(recipe with { parameterExpression = "{nativeType}({missing})" }));
        Assert.Throws<NotSupportedException>(() => new LoweringRecipeCompiler().Compile(recipe with { managedProjection = new("int", "{value}", null) }));
    }

    private static TypeLoweringRecipe Recipe() => new()
    {
        id = "range", typePattern = "Demo::Range", cAbiType = "uint64_t", abiParameters = [new("data", "const int32_t*"), new("length", "uint64_t")],
        parameterExpression = "{nativeType}({data}, {length})"
    };

    private static NativeBridgePlan Plan()
    {
        SourceLocation location = new("fixture.hpp", 1, 1);
        NativeType integer = new(new("int"), NativeTypeCategory.Primitive, "int", 4, 4) { primitiveKind = "Int", isSigned = true };
        NativeType range = new(new("range"), NativeTypeCategory.Record, "Demo::Range", 16, 8) { declaration = new("range-declaration") };
        NativeDeclaration record = new(new("range-declaration"), "Class", "Range", "Demo::Range", range.id, location) { isCompleteDefinition = true };
        NativeDeclaration callable = new(new("sum"), "Function", "Sum", "Demo::Sum", null, location) { returnType = integer.id, children = [new("range-parameter"), new("bias")] };
        NativeDeclaration input = new(new("range-parameter"), "Parameter", "range", "range", range.id, location) { parent = callable.id };
        NativeDeclaration bias = new(new("bias"), "Parameter", "bias", "bias", integer.id, location) { parent = callable.id };
        NativeModule module = new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8, [integer, range], [record, callable, input, bias], []) { language = SourceLanguage.Cpp };
        NativeTargetDescriptor target = new(new(module.targetId), "windows", "x64", "msvc", module.triple, new());
        return new NativeBridgePlanner(new LoweringRegistry([(new global::BGCS.Core.Plugins.ExtensionDescriptor("fixture.recipe", global::BGCS.Core.Plugins.ExtensionPort.NativeTypeLowering), new LoweringRecipeCompiler().Compile(Recipe()))])).Plan(new(module, target, "fixture", ["fixture.hpp"]));
    }

    private sealed class Sink : IArtifactSink
    {
        internal Dictionary<string, string> files { get; } = [];
        public string WriteText(string relativePath, string content) { files.Add(relativePath, content); return relativePath; }
    }
}
