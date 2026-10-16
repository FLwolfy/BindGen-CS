using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Analysis.Bridge;
using BGCS.Analysis.Declarations;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Native;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.Cpp2C.Emission;
using BGCS.Cpp2C.Planning;
using Xunit;

namespace BGCS.Cpp2C.Tests;

public sealed class ConfiguredExactTypeFamilyTests
{
    [Fact]
    public void SelectedCallableRetainsPublicMemberAliasFactsWithoutPromotingThemToApiRoots()
    {
        LoweringContext context = Context();
        DeclarationSelection selected = new DeclarationSelector().Select(context.module, new() { includeDeclarations = ["Make"] }, ["exact.hpp"], System.IO.Directory.GetCurrentDirectory());
        Assert.Contains(new StableId("unit-declaration"), selected.module.GetDeclaration(new("text-declaration")).children);
        Assert.Equal(new StableId("char"), new StandardLibraryFactReader([Binding("custom::TextAlias")]).Read(selected.module, new("text"))!.argumentTypes[0]);
        Assert.Equal(new StableId("make"), Assert.Single(selected.publicDeclarations));
        Assert.Equal(new StableId("make"), Assert.Single(selected.callables));
    }

    [Fact]
    public void RequiredPublicMemberAliasStillHonorsExplicitExclusions()
    {
        LoweringContext context = Context();
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => new DeclarationSelector().Select(context.module,
            new() { includeDeclarations = ["Make"], excludeDeclarations = ["custom::Text::value_type"] }, ["exact.hpp"], System.IO.Directory.GetCurrentDirectory()));
        Assert.Contains("required", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SystemClassDependenciesDoNotExpandAllPublicMemberAliases()
    {
        LoweringContext context = Context();
        NativeModule module = context.module;
        module = new(module.targetId, module.triple, module.pointerSize, module.types,
            module.declarations.Select(declaration => declaration.id == new StableId("text-declaration") ? declaration with { isSystemDeclaration = true } : declaration).ToImmutableArray(), []) { language = module.language };
        DeclarationSelection selected = new DeclarationSelector().Select(module, new() { includeDeclarations = ["Make"] }, ["exact.hpp"], System.IO.Directory.GetCurrentDirectory());
        Assert.DoesNotContain(selected.module.declarations, declaration => declaration.id == new StableId("unit-declaration"));
        Assert.Single(selected.callables);
    }

    [Theory]
    [InlineData("custom::Text")]
    [InlineData("::custom::TextAlias")]
    public void ExactClassAndAliasKeepActualNonTemplateStringIdentity(string selector)
    {
        LoweringContext context = Context();
        CppFamilyBinding binding = Binding(selector);
        StandardValueOperation facts = new StandardLibraryFactReader([binding]).Read(context.module, new("text-alias"))!;
        Assert.Equal(new StableId("char"), Assert.Single(facts.argumentTypes));
        CppBridgeModule bridge = new CppBridgePlanner(familyBindings: [binding]).Plan(context);
        Assert.Contains(bridge.types, type => type.kind == BridgeTypeKind.Handle && type.nativeType == new StableId("text"));
        Assert.DoesNotContain(bridge.types, type => type.kind == BridgeTypeKind.Record && type.nativeType == new StableId("text"));
        Sink sink = new();
        new TypedBridgeEmitter().Emit(bridge, sink, "exact");
        Assert.Contains("using bgcs_family_proof_0_owner = ::custom::Text;", sink.files["src/exact.cpp"]);
        Assert.Contains("exact native element type", sink.files["src/exact.cpp"]);
        Assert.DoesNotContain("reinterpret_cast<::std::", sink.files["src/exact.cpp"]);
    }

    [Fact]
    public void SameImmutableModuleCanUseDistinctFrozenBindingConfigurations()
    {
        NativeModule module = Context().module;
        List<CppFamilyBinding> settings = [Binding("custom::TextAlias")];
        StandardLibraryFactReader text = new(settings);
        settings[0] = PathBinding();
        StandardLibraryFactReader path = new(settings);
        Assert.Equal(StandardValueKind.String, text.Read(module, new("text"))!.family);
        Assert.Equal(StandardValueKind.Path, path.Read(module, new("text-alias"))!.family);
        Assert.Empty(path.Read(module, new("text"))!.argumentTypes);
        StandardLibraryFactReader incompatibleEncoding = new([Binding("custom::Text") with { stringEncoding = NativeTextEncoding.Utf16 }]);
        Assert.Throws<NotSupportedException>(() => incompatibleEncoding.Read(module, new("text")));
        Assert.Equal(StandardValueKind.String, text.Read(module, new("text-alias"))!.family);
        Assert.Equal(StandardValueKind.Path, path.Read(module, new("text"))!.family);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("both")]
    [InlineData("blank")]
    [InlineData("wildcard")]
    [InlineData("unrelated-family")]
    [InlineData("same-selector")]
    public void ExactSelectorsCannotBeAmbiguousOrApplyToUnrelatedProtocols(string mutation)
    {
        CppFamilyBinding binding = Binding("custom::Text");
        ImmutableArray<CppFamilyBinding> settings = mutation switch
        {
            "none" => [binding with { nativeType = null }],
            "both" => [binding with { nativeTemplate = "custom::Text" }],
            "blank" => [binding with { nativeType = " " }],
            "wildcard" => [binding with { nativeType = "custom::*" }],
            "unrelated-family" => [binding with { family = StandardValueKind.Vector, stringEncoding = NativeTextEncoding.Unknown }],
            "same-selector" => [binding, binding with { nativeType = "::custom::Text" }],
            _ => throw new ArgumentException(nameof(mutation))
        };
        Assert.ThrowsAny<Exception>(() => new CppBridgePlanner(familyBindings: settings));
    }

    [Theory]
    [InlineData("alias-conflict")]
    [InlineData("missing")]
    [InlineData("template-class")]
    [InlineData("private-unit")]
    [InlineData("missing-unit")]
    [InlineData("wrong-width")]
    public void ActualSourceFactsMustEstablishOneClassAndAccessibleCodeUnit(string mutation)
    {
        LoweringContext context = Context();
        ImmutableArray<CppFamilyBinding> settings = [Binding(mutation == "missing" ? "custom::Missing" : "custom::Text")];
        if (mutation == "alias-conflict") settings = settings.Add(Binding("custom::TextAlias"));
        if (mutation == "wrong-width") settings = [settings[0] with { stringEncoding = NativeTextEncoding.Utf32 }];
        if (mutation is "template-class" or "private-unit" or "missing-unit")
        {
            NativeModule module = context.module;
            context = context with { module = new(module.targetId, module.triple, module.pointerSize, module.types,
                module.declarations.Select(declaration => declaration.id == new StableId("text-declaration")
                    ? mutation == "template-class" ? declaration with { templateArguments = [new("Type", "char", new("char"))] }
                        : mutation == "missing-unit" ? declaration with { children = [] } : declaration
                    : mutation == "private-unit" && declaration.id == new StableId("unit-declaration") ? declaration with { visibility = "Private" } : declaration).ToImmutableArray(), [])
                { language = module.language } };
        }
        Assert.ThrowsAny<Exception>(() => new CppBridgePlanner(familyBindings: settings).Plan(context));
    }

    [Theory]
    [InlineData("remove")]
    [InlineData("change-protocol")]
    public void IndependentVerifierRejectsChangedExactBindingsBeforeSourceWrites(string mutation)
    {
        CppBridgeModule bridge = new CppBridgePlanner(familyBindings: [Binding("custom::TextAlias")]).Plan(Context());
        bridge = bridge with { familyBindings = mutation == "remove" ? [] : [PathBinding()] };
        Sink sink = new();
        Assert.ThrowsAny<Exception>(() => new TypedBridgeEmitter().Emit(bridge, sink, "exact"));
        Assert.Empty(sink.files);
    }

    private static CppFamilyBinding Binding(string type) => new() { nativeType = type, family = StandardValueKind.String,
        stringEncoding = NativeTextEncoding.Utf8, evidence = new(EvidenceLevel.UserAsserted, "exact-string-protocol") };
    private static CppFamilyBinding PathBinding() => new() { nativeType = "custom::TextAlias", family = StandardValueKind.Path,
        pathFormat = NativePathFormat.GenericUtf8, evidence = new(EvidenceLevel.UserAsserted, "exact-path-protocol") };

    private static LoweringContext Context()
    {
        SourceLocation source = new("exact.hpp", 1, 1);
        NativeType unit = new(new("char"), NativeTypeCategory.Primitive, "char", 1, 1) { primitiveKind = "Char", isSigned = true };
        NativeType text = new(new("text"), NativeTypeCategory.Record, "custom::Text", 24, 8) { declaration = new("text-declaration") };
        NativeType unitAlias = new(new("unit-alias"), NativeTypeCategory.Alias, "custom::Text::value_type", 1, 1)
            { elementType = unit.id, canonicalType = unit.id, declaration = new("unit-declaration") };
        NativeType textAlias = new(new("text-alias"), NativeTypeCategory.Alias, "custom::TextAlias", 24, 8)
            { elementType = text.id, canonicalType = text.id, declaration = new("alias-declaration") };
        NativeDeclaration owner = new(new("text-declaration"), "Class", "Text", text.spelling, text.id, source)
        { role = "Class", isCompleteDefinition = true, isDefaultConstructible = true, children = [new("unit-declaration")] };
        NativeDeclaration alias = new(new("alias-declaration"), "Typedef", "TextAlias", textAlias.spelling, textAlias.id, source);
        NativeDeclaration valueType = new(new("unit-declaration"), "Typedef", "value_type", unitAlias.spelling, unitAlias.id, source)
            { parent = owner.id, visibility = "Public" };
        NativeDeclaration make = new(new("make"), "Function", "Make", "Make", null, source) { returnType = textAlias.id };
        NativeModule module = new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8, [unit, text, unitAlias, textAlias], [owner, alias, valueType, make], []) { language = SourceLanguage.Cpp };
        NativeTargetDescriptor target = new(new(module.targetId), "windows", "x64", "msvc", module.triple, new());
        return new(module, target, "exact", ["exact.hpp"]);
    }

    private sealed class Sink : IArtifactSink
    {
        internal Dictionary<string, string> files { get; } = [];
        public string WriteText(string relativePath, string content) { files.Add(relativePath, content); return relativePath; }
    }
}
