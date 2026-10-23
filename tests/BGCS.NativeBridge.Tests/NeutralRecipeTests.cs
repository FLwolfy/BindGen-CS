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

public sealed class NeutralRecipeTests
{
    [Fact]
    public void ConfiguredExpressions_UseExactContentIdentitiesAndEvaluateNativeResultOnce()
    {
        var lowering = new LoweringRecipeCompiler().Compile(new()
        {
            id = "quantity",
            typePattern = "Demo::Quantity",
            cAbiType = "int32_t",
            parameterExpression = "{nativeType}({value} + 1)",
            resultExpression = "static_cast<int>({value}) - 1"
        });
        NativeBridgePlan module = new NativeBridgePlanner(new LoweringRegistry([(new global::BGCS.Core.Plugins.ExtensionDescriptor("fixture.recipe", global::BGCS.Core.Plugins.ExtensionPort.NativeTypeLowering), lowering)]), [new()
        { id = "observe", functionPattern = "Demo::Echo", exportName = "fixture_echo", invocationExpression = "observe({invocation})" }]).Plan(Context());
        BridgeExport export = Assert.Single(module.exports);
        Assert.Equal("fixture_echo", export.name);
        Assert.Equal(BridgeConversionKind.SourceExpression, export.parameters[0].conversion);
        Assert.Equal(EvidenceLevel.UserAsserted, export.parameters[0].conversionExpression!.evidence.level);
        Sink sink = new();
        new TypedBridgeEmitter().Emit(module, sink, "fixture");
        string source = sink.files["src/fixture.cpp"];
        Assert.Contains("Demo::Quantity(arg0 + 1)", source);
        Assert.Contains("auto&& bgcs_native_result = ((observe(::Demo::Echo(", source);
        Assert.Contains("static_cast<int>(bgcs_native_result) - 1", source);
        Assert.Equal(1, source.Split("::Demo::Echo(", StringSplitOptions.None).Length - 1);
    }

    [Theory]
    [InlineData("content")]
    [InlineData("placeholder")]
    [InlineData("evidence")]
    [InlineData("dependency")]
    [InlineData("header")]
    public void ExplicitSourceFragments_AreValidatedBeforeAnyCandidateWrite(string mutation)
    {
        var lowering = new LoweringRecipeCompiler().Compile(new()
        { id = "quantity", typePattern = "Demo::Quantity", cAbiType = "int32_t", parameterExpression = "{nativeType}({value})", resultExpression = "static_cast<int>({value})" });
        NativeBridgePlan module = new NativeBridgePlanner(new LoweringRegistry([(new global::BGCS.Core.Plugins.ExtensionDescriptor("fixture.recipe", global::BGCS.Core.Plugins.ExtensionPort.NativeTypeLowering), lowering)])).Plan(Context());
        BridgeExport export = module.exports[0];
        ControlledSourceFragment expression = export.parameters[0].conversionExpression!;
        expression = mutation switch
        {
            "content" => expression with { text = expression.text + " + 1" },
            "placeholder" => LoweringRecipeCompiler.Expression("quantity", "{unregistered}({value})"),
            "evidence" => expression with { evidence = new(EvidenceLevel.Verified, "asserted-by-user") },
            "dependency" => expression with { typeDependencies = [new("missing-type")] },
            "header" => expression with { requiredHeaders = ["undeclared.hpp"] },
            _ => throw new ArgumentException(nameof(mutation))
        };
        module = module with { exports = [export with { parameters = [export.parameters[0] with { conversionExpression = expression }] }] };
        Sink sink = new();
        Assert.ThrowsAny<Exception>(() => new TypedBridgeEmitter().Emit(module, sink, "fixture"));
        Assert.Empty(sink.files);
    }

    [Fact]
    public void ExplicitExclusion_RunsBeforeUnsupportedTypePlanning()
    {
        NativeBridgePlan module = new NativeBridgePlanner(callableRecipes: [new() { id = "exclude", functionPattern = "Demo::Echo", exclude = true }]).Plan(Context());
        Assert.Empty(module.exports);
    }

    [Fact]
    public void SamePriorityCallableRecipes_RejectExclusiveConflict()
    {
        Assert.Throws<InvalidOperationException>(() => new NativeBridgePlanner(callableRecipes:
            [new() { id = "first", functionPattern = "Echo", exclude = true }, new() { id = "second", functionPattern = "Demo::*", exclude = true }]).Plan(Context()));
    }

    [Theory]
    [InlineData("{invocation} + {invocation}")]
    [InlineData("unrelated()")]
    public void CallableRecipe_MustWrapExactlyOnePlannedNativeCall(string expression)
        => Assert.Throws<ArgumentException>(() => new NativeBridgePlanner(callableRecipes: [new() { id = "invalid", functionPattern = "*", invocationExpression = expression }]));

    [Theory]
    [InlineData("long double")]
    [InlineData("int")]
    [InlineData("void")]
    [InlineData("int32_t**")]
    public void DeclarativeCarrier_RejectsUnprovedOrUnboundedStorage(string carrier)
        => Assert.Throws<NotSupportedException>(() => new LoweringRecipeCompiler().Compile(new() { id = "invalid", typePattern = "*", cAbiType = carrier }));

    [Theory]
    [InlineData(4, "size_t", "uint32_t")]
    [InlineData(8, "size_t", "uint64_t")]
    [InlineData(4, "ptrdiff_t", "int32_t")]
    [InlineData(8, "intptr_t", "int64_t")]
    public void NativeSizeRecipeAliases_UseTheParsedTargetWidth(
        int pointerSize,
        string alias,
        string expected
    ) {
        TypeLoweringRecipe recipe = new() { id = "size", typePattern = "*", cAbiType = alias };
        var carrier = new LoweringRecipeCompiler().GetCarrier(recipe, pointerSize);
        Assert.Equal(expected, Assert.Single(carrier.types).cName);
    }

    private static LoweringContext Context()
    {
        SourceLocation location = new("fixture.hpp", 1, 1);
        NativeType quantity = new(new("quantity"), NativeTypeCategory.Record, "Demo::Quantity", 4, 4) { declaration = new("quantity-class") };
        NativeDeclaration owner = new(new("quantity-class"), "Class", "Quantity", "Demo::Quantity", quantity.id, location);
        NativeDeclaration parameter = new(new("parameter"), "Parameter", "value", "value", quantity.id, location) { parent = new("echo") };
        NativeDeclaration echo = new(new("echo"), "Function", "Echo", "Demo::Echo", null, location) { returnType = quantity.id, children = [parameter.id] };
        NativeModule source = new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8, [quantity], [owner, parameter, echo], []) { language = SourceLanguage.Cpp };
        NativeTargetDescriptor target = new(new(source.targetId), "windows", "x64", "msvc", source.triple, new());
        return new(source, target, "fixture", ["fixture.hpp"]);
    }

    private sealed class Sink : IArtifactSink
    {
        internal Dictionary<string, string> files { get; } = [];
        public string WriteText(
            string relativePath,
            string content
        ) { files.Add(relativePath, content); return relativePath; }
    }
}
