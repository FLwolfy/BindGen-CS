using System;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Analysis.Declarations;
using BGCS.Analysis.Interop;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Native;
using Xunit;

namespace BGCS.Cpp2C.Tests;

public sealed class NeutralMacroAliasTests
{
    [Fact]
    public void SelectedMacroRetainsItsExactFunctionDependencyAndReferencesOnePrimaryExport()
    {
        NativeModule source = Source();
        var selection = new DeclarationSelector().Select(source, new() { includeDeclarations = ["PublicAdd"] }, ["aliases.h"], System.IO.Path.GetTempPath());
        Assert.Equal(new[] { new StableId("function") }, selection.callables);
        Assert.Contains(selection.module.declarations, declaration => declaration.id == new StableId("function"));
        InteropFunction primary = new(new("export"), "actual_add", new("int"), [], "C") { sourceDeclaration = new("function") };
        InteropFunction adapter = primary with { id = new("adapter"), exportName = "sequence_add", inputAdapterOf = primary.id };
        InteropModule abi = new("aliases", source.targetId, source.pointerSize, [], [primary, adapter], []);
        InteropModule result = new NativeFunctionAliasAnalyzer().Apply(selection.module, abi, selection.publicDeclarations);
        Assert.Empty(result.diagnostics);
        InteropFunctionAlias alias = Assert.Single(result.functionAliases);
        Assert.Equal("PublicAdd", alias.name);
        Assert.Equal(primary.id, alias.nativeFunction);
        Assert.Equal(2, result.functions.Length);
        Assert.Equal(EvidenceLevel.Declared, alias.evidence.level);
    }

    [Fact]
    public void ExplicitExclusionOfAnAliasedFunctionFailsWithItsDependencyChain()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => new DeclarationSelector().Select(Source(),
            new() { includeDeclarations = ["PublicAdd"], excludeDeclarations = ["Add"] }, ["aliases.h"], System.IO.Path.GetTempPath()));
        Assert.Contains("dependency chain", error.Message);
        Assert.Contains("Add", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MissingOrMultiplePrimaryExportsProduceAnExplicitAliasError(int matches)
    {
        NativeModule source = Source();
        ImmutableArray<InteropFunction> exports = Enumerable.Range(0, matches).Select(index => new InteropFunction(new("export" + index), "export" + index, new("int"), [], "C")
        { sourceDeclaration = new("function") }).ToImmutableArray();
        InteropModule result = new NativeFunctionAliasAnalyzer().Apply(source, new("aliases", source.targetId, source.pointerSize, [], exports, []));
        Assert.Empty(result.functionAliases);
        Assert.Equal("BGCS-MACRO-ALIAS", Assert.Single(result.diagnostics).code);
        Assert.Equal("aliases.h", Assert.Single(result.diagnostics).location!.Value.file);
    }

    [Fact]
    public void SemanticTransformsCannotRetargetAnEstablishedAliasOrLeaveItDangling()
    {
        NativeModule source = Source();
        InteropFunction first = new(new("first"), "first", new("int"), [], "C") { sourceDeclaration = new("function") };
        InteropFunction other = first with { id = new("other"), exportName = "other", sourceDeclaration = new("other-source") };
        InteropModule baseline = new NativeFunctionAliasAnalyzer().Apply(source, new("aliases", source.targetId, source.pointerSize, [], [first, other], []));
        InteropFunctionAlias alias = Assert.Single(baseline.functionAliases);
        InteropModule changed = new("aliases", source.targetId, source.pointerSize, [], baseline.functions, []) { functionAliases = [alias with { nativeFunction = other.id }] };
        Assert.Throws<InvalidOperationException>(() => new InteropModuleVerifier().VerifyPreservedAbi(baseline, changed));
        InteropModule dangling = new("aliases", source.targetId, source.pointerSize, [], [other], []) { functionAliases = baseline.functionAliases };
        Assert.Throws<InvalidOperationException>(() => new InteropModuleVerifier().VerifyPreservedAbi(baseline, dangling));
    }

    private static NativeModule Source()
    {
        NativeType integer = new(new("int"), NativeTypeCategory.Primitive, "int", 4, 4) { primitiveKind = "Int", isSigned = true };
        NativeDeclaration function = new(new("function"), "Function", "Add", "Add", null, new("aliases.h", 1, 1))
        { returnType = integer.id, linkage = "C", callingConvention = "C", nativeLinkage = NativeLinkageKind.External, role = "Function" };
        NativeDeclaration alias = new(new("alias"), "Macro", "PublicAdd", "PublicAdd", null, new("aliases.h", 2, 1))
        { value = "Add", macro = new(false, [], true) { functionAlias = function.id, functionAliasEvidence = new(EvidenceLevel.Declared, "compiler:macro-reference") } };
        return new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8, [integer], [function, alias], []) { language = SourceLanguage.C };
    }
}
