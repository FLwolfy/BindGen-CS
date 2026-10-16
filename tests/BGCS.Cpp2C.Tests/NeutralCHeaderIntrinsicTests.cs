using System;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Analysis.Bridge;
using BGCS.Analysis.Interop;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using Xunit;

namespace BGCS.Cpp2C.Tests;

public sealed class NeutralCHeaderIntrinsicTests
{
    [Fact]
    public void DirectCallOnlyCompilerFactRequiresAnActualWrapperWithoutInventingASourceDefinition()
    {
        NativeModule module = Module();
        NativeDeclaration callable = module.GetDeclaration(new("intrinsic"));
        Assert.False(callable.isDefinition);
        var wrapper = Assert.Single(new CHeaderBridgePlanner().Plan(module, [callable.id], "test").exports);
        Assert.Equal(callable.id, wrapper.sourceDeclaration);
        Assert.NotEqual(callable.name, wrapper.exportName);
        Assert.Empty(new CDeclarationAnalyzer().Analyze(module, "test").functions);
        Assert.Equal(wrapper.exportName, Assert.Single(new CDeclarationAnalyzer().Analyze(module, "test", [callable.id], new("test", [wrapper])).functions).exportName);
    }

    [Theory]
    [InlineData("parameter")]
    [InlineData("result")]
    [InlineData("variadic")]
    [InlineData("unspecified")]
    [InlineData("evidence")]
    public void UnsupportedIntrinsicShapesAndMissingProvenanceAreRefused(string invalid)
    {
        NativeModule source = Module();
        NativeDeclaration intrinsic = source.GetDeclaration(new("intrinsic"));
        NativeDeclaration changed = invalid switch
        {
            "parameter" => intrinsic with { children = [new("parameter")] },
            "result" => intrinsic with { returnType = new("int") },
            "variadic" => intrinsic with { isVariadic = true },
            "unspecified" => intrinsic with { hasPrototype = false },
            _ => intrinsic with { addressabilityEvidence = null }
        };
        NativeModule module = new(source.targetId, source.triple, source.pointerSize, source.types,
            source.declarations.Select(declaration => declaration.id == intrinsic.id ? changed : declaration).ToImmutableArray(), []) { language = SourceLanguage.C };
        Assert.Throws<NotSupportedException>(() => new CHeaderBridgePlanner().Plan(module, [intrinsic.id], "test"));
    }

    private static NativeModule Module()
    {
        SourceLocation source = new("intrinsics.h", 1, 1);
        NativeDeclaration callable = new(new("intrinsic"), "Function", "CompilerAction", "CompilerAction", null, source)
        {
            returnType = new("void"), callingConvention = "C", linkage = "C", nativeLinkage = NativeLinkageKind.External,
            addressability = NativeCallableAddressability.DirectCallOnly,
            addressabilityEvidence = new(EvidenceLevel.Declared, "frontend:test:address-expression")
        };
        NativeDeclaration parameter = new(new("parameter"), "Parameter", "value", "CompilerAction::value", new("int"), source) { parent = callable.id };
        NativeType none = new(new("void"), NativeTypeCategory.Primitive, "void", 0, null) { primitiveKind = "Void" };
        NativeType integer = new(new("int"), NativeTypeCategory.Primitive, "int", 4, 4) { primitiveKind = "Int", isSigned = true };
        return new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8, [none, integer], [callable, parameter], []) { language = SourceLanguage.C };
    }
}
