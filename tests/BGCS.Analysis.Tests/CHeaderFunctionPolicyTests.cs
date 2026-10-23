using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using BGCS.Analysis.Bridge;
using BGCS.Analysis.Declarations;
using BGCS.Analysis.Interop;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using Xunit;

namespace BGCS.Analysis.Tests;

public sealed class CHeaderFunctionPolicyTests
{
    [Fact]
    public void DefaultSelectionKeepsExternalAliasesWithoutDemandingHeaderHelperExports()
    {
        DeclarationSelection selection = Select();
        Assert.Equal(new[] { "external" }, selection.callables.Select(id => id.value));
        Assert.Equal(new[] { "external", "external-alias" }, selection.publicDeclarations.Select(id => id.value));
        Assert.DoesNotContain(selection.module.declarations, declaration => CHeaderFunctionPolicy.RequiresAdapter(declaration) || declaration.id.value == "helper-alias");
        var interop = new CDeclarationAnalyzer().Analyze(selection.module, "library", selection.callables);
        interop = new NativeFunctionAliasAnalyzer().Apply(selection.module, interop, selection.publicDeclarations);
        Assert.Empty(interop.diagnostics);
        Assert.Equal("External", Assert.Single(interop.functions).exportName);
        Assert.Equal(new StableId("external"), Assert.Single(interop.functionAliases).nativeFunction);
    }

    [Fact]
    public void OptInRetainsAllHelperShapesAndTheirExactAliases()
    {
        DeclarationSelection selection = Select(include: true);
        Assert.Equal(5, selection.callables.Length);
        Assert.Contains(new StableId("helper-alias"), selection.publicDeclarations);
        var interop = new CDeclarationAnalyzer().Analyze(selection.module, "fixture", selection.callables, CDeclarationAnalysisPurpose.NativeBridgePlanning);
        Assert.Equal(5, interop.functions.Length);
        Assert.Equal(4, selection.callables.Select(selection.module.GetDeclaration).Count(CHeaderFunctionPolicy.RequiresAdapter));
    }

    [Theory]
    [InlineData("static")]
    [InlineData("inline")]
    [InlineData("internal")]
    [InlineData("intrinsic")]
    [InlineData("helper-alias")]
    public void ExactHelperSelectorsAndCleanupRequireTheActionableOptIn(string selector)
    {
        foreach (string channel in new[] { "global", "family", "cleanup" })
        {
            DeclarationSelectionRequest request = channel switch
            {
                "global" => new() { includeDeclarations = [selector] },
                "family" => new() { categories = [new(NativeDeclarationCategory.Callables) { includeDeclarations = [selector] }] },
                _ => new()
            };
            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => Select(request, required: channel == "cleanup" ? [selector] : []));
            Assert.Contains("source.includeHeaderHelpers", failure.Message, StringComparison.Ordinal);
            Assert.Contains("configured native module", failure.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ExplicitlyGeneratedFixedAdapterSurvivesWithoutEnablingOrdinaryHelpers()
    {
        DeclarationSelection selection = Select(additional: ["static"]);
        Assert.Contains(new StableId("static"), selection.callables);
        Assert.DoesNotContain(new StableId("inline"), selection.callables);
        Assert.DoesNotContain(new StableId("helper-alias"), selection.publicDeclarations);
        var interop = new CDeclarationAnalyzer().Analyze(selection.module, "fixture", selection.callables, CDeclarationAnalysisPurpose.NativeBridgePlanning);
        Assert.Contains(interop.functions, function => function.id == new StableId("static"));
        Assert.Single(selection.callables.Select(selection.module.GetDeclaration), CHeaderFunctionPolicy.RequiresAdapter);
    }

    [Fact]
    public void DirectCAnalysisAlsoRefusesAnInlineNameWithoutACompiledAdapter()
    {
        NativeModule module = Module();
        var interop = new CDeclarationAnalyzer().Analyze(module, "fixture", [new("inline")]);
        Assert.Empty(interop.functions);
        var diagnostic = Assert.Single(interop.diagnostics, value => value.isFailure);
        Assert.Equal(module.GetDeclaration(new("inline")).source, diagnostic.location);
        Assert.Contains("source.includeHeaderHelpers", diagnostic.message, StringComparison.Ordinal);
    }

    private static DeclarationSelection Select(DeclarationSelectionRequest? request = null, bool include = false,
        ImmutableArray<string> required = default, ImmutableArray<string> additional = default)
        => new DeclarationSelector(StringComparer.Ordinal).Select(Module(), request ?? new(), [Header], Path.GetDirectoryName(Header)!,
            requiredDeclarations: required.IsDefault ? [] : required, additionalPublicDeclarations: additional.IsDefault ? [] : additional,
            includeCHeaderHelpers: include);

    private static readonly string Header = Path.Combine(Path.GetTempPath(), "bgcs-neutral-header-policy", "library.h");

    private static NativeModule Module()
    {
        SourceLocation source = new(Header, 3, 1);
        NativeType none = new(new("void"), NativeTypeCategory.Primitive, "void", 0, null) { primitiveKind = "Void" };
        NativeDeclaration external = Function("external", "External");
        NativeDeclaration statik = Function("static", "Static") with { isStatic = true, isDefinition = true };
        NativeDeclaration inline = Function("inline", "Inline") with { isInline = true, isDefinition = true };
        NativeDeclaration internalLinkage = Function("internal", "Internal") with { nativeLinkage = NativeLinkageKind.Internal, isDefinition = true };
        NativeDeclaration intrinsic = Function("intrinsic", "Intrinsic") with { addressability = NativeCallableAddressability.DirectCallOnly,
            addressabilityEvidence = new(EvidenceLevel.Declared, "fixture:addressability") };
        return new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8, [none],
            [external, statik, inline, internalLinkage, intrinsic, Alias("external-alias", external), Alias("helper-alias", inline)], [])
        { language = SourceLanguage.C };

        NativeDeclaration Function(string id, string name) => new(new(id), "Function", name, name, null, source)
        { returnType = none.id, nativeLinkage = NativeLinkageKind.External, callingConvention = "C", linkage = "C" };
        NativeDeclaration Alias(string id, NativeDeclaration target) => new(new(id), "Macro", id, id, null, source)
        { macro = new(false, [], true) { functionAlias = target.id, functionAliasEvidence = new(EvidenceLevel.Declared, "fixture:alias") } };
    }
}
