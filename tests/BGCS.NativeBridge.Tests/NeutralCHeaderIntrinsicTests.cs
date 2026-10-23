using System;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Analysis.Interop;
using BGCS.Core.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.Core.Targets;
using BGCS.NativeBridge.Planning;
using Xunit;

namespace BGCS.NativeBridge.Tests;

public sealed class NeutralCHeaderIntrinsicTests
{
    [Fact]
    public void DirectCallOnlyCompilerFactUsesTheOrdinaryTypedNativeBridgeWithoutInventingASourceDefinition()
    {
        NativeModule module = Module();
        NativeDeclaration callable = module.GetDeclaration(new("intrinsic"));
        Assert.False(callable.isDefinition);
        BridgeExport wrapper = Assert.Single(Plan(module).exports);
        Assert.Equal(callable.id, wrapper.operation.declaration);
        Assert.NotEqual(callable.name, wrapper.name);
        Assert.False(wrapper.usesErrorChannel);
        Assert.Empty(new CDeclarationAnalyzer().Analyze(module, "test").functions);
        Assert.Equal(callable.id, Assert.Single(new CDeclarationAnalyzer().Analyze(module, "test", [callable.id], CDeclarationAnalysisPurpose.NativeBridgePlanning).functions).id);
    }

    [Theory]
    [InlineData("parameter")]
    [InlineData("result")]
    public void ExactScalarIntrinsicInputsAndResultsUseOrdinaryTypedChannels(string shape)
    {
        NativeModule original = Module();
        NativeDeclaration function = original.GetDeclaration(new("intrinsic"));
        function = shape == "parameter" ? function with { children = [new("parameter")] } : function with { returnType = new("int") };
        NativeModule module = new(original.targetId, original.triple, original.pointerSize, original.types,
            original.declarations.Replace(original.GetDeclaration(function.id), function).AddRange(shape == "parameter"
                ? ImmutableArray.Create(new NativeDeclaration(new("parameter"), "Parameter", "value", "CompilerAction::value", new("int"), function.source) { parent = function.id }) : []), []) { language = SourceLanguage.C };
        BridgeExport export = Assert.Single(Plan(module).exports);
        Assert.Equal(shape == "parameter" ? 1 : 0, export.parameters.Length);
        Assert.Equal(shape == "result", export.resultType is not null);
        Assert.False(export.resultInOutputParameter);
    }

    [Theory]
    [InlineData("variadic")]
    [InlineData("unspecified")]
    [InlineData("evidence")]
    public void UnspecifiedSignaturesAndMissingCompilerProvenanceRemainRefused(string invalid)
    {
        NativeModule source = Module();
        NativeDeclaration intrinsic = source.GetDeclaration(new("intrinsic"));
        NativeDeclaration changed = invalid switch
        {
            "variadic" => intrinsic with { isVariadic = true },
            "unspecified" => intrinsic with { hasPrototype = false },
            _ => intrinsic with { addressabilityEvidence = null }
        };
        NativeModule module = new(source.targetId, source.triple, source.pointerSize, source.types,
            source.declarations.Replace(intrinsic, changed), []) { language = SourceLanguage.C };
        if (invalid == "evidence") Assert.Throws<InvalidOperationException>(() => Plan(module));
        else Assert.Throws<NotSupportedException>(() => Plan(module));
    }
    private static NativeBridgePlan Plan(NativeModule module)
        => new NativeBridgePlanner().Plan(new(module,
            new(new(module.targetId), "fixture", "x64", "fixture", module.triple, new()) { sourceConventions = new(string.Empty, string.Empty, new("Win64", true)) }, "test", []));
    private static NativeModule Module()
    {
        SourceLocation source = new("intrinsics.h", 1, 1);
        NativeDeclaration callable = new(new("intrinsic"), "Function", "CompilerAction", "CompilerAction", null, source)
        {
            returnType = new("void"), callingConvention = "C", linkage = "C", nativeLinkage = NativeLinkageKind.External,
            addressability = NativeCallableAddressability.DirectCallOnly,
            addressabilityEvidence = new(EvidenceLevel.Declared, "frontend:test:address-expression")
        };

        NativeType none = new(new("void"), NativeTypeCategory.Primitive, "void", 0, null) { primitiveKind = "Void" };
        NativeType integer = new(new("int"), NativeTypeCategory.Primitive, "int", 4, 4) { primitiveKind = "Int", isSigned = true };
        return new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8, [none, integer], [callable], []) { language = SourceLanguage.C };
    }
}
