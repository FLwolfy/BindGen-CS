using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Analysis.Bridge;
using BGCS.Analysis.Declarations;
using BGCS.Core.Artifacts;
using BGCS.Core.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.Core.Targets;
using BGCS.NativeBridge.Emission;
using BGCS.NativeBridge.Planning;
using Xunit;

namespace BGCS.NativeBridge.Tests;

public sealed class StandardPathInputTests
{
    [Fact]
    public void CompilerUnitProbeSurvivesAsMetadataWithoutPublishingItsPrivateAlias()
    {
        NativeModule original = Context(false).module;
        NativeType aliasType = new(new("probe-type"), NativeTypeCategory.Alias, StandardPathInputProbe.C_ALIAS, 1, 1) { elementType = new("char8"), declaration = new("probe") };
        NativeDeclaration alias = new(new("probe"), "Typedef", "Utf8Unit", StandardPathInputProbe.C_ALIAS, aliasType.id, new("paths.hpp", 2, 1));
        NativeModule source = new(original.targetId, original.triple, original.pointerSize, original.types.Add(aliasType), original.declarations.Add(alias), []) { language = original.language };
        StableId probe = StandardPathInputProbe.Find(source)!.Value;
        DeclarationSelection selection = new DeclarationSelector(global::System.StringComparer.Ordinal).Select(source, new() { includeDeclarations = ["Check"], excludeCategories = [NativeDeclarationCategory.TypeAliases] }, ["paths.hpp"], Environment.CurrentDirectory,
            ordinaryDeclarations: original.declarations.Select(declaration => declaration.id), requiredNativeTypes: [StandardLibraryFactReader.Resolve(source, source.GetDeclaration(probe).type!.Value).id]);
        Assert.Equal(new StableId("char8"), StandardPathInputFacts.ReadElement(selection.module, new("path")));
        Assert.DoesNotContain(selection.publicDeclarations, id => id == probe);
        Assert.DoesNotContain(selection.module.declarations, declaration => declaration.id == probe);
        Assert.Single(selection.callables);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("signed")]
    [InlineData("wide")]
    public void MissingOrContradictoryCompilerProbeCannotInventUtf8Units(string mutation)
    {
        NativeModule original = Context(false).module;
        NativeDeclaration alias = new(new("probe"), "Typedef", "Utf8Unit", StandardPathInputProbe.C_ALIAS, new("char8"), new("paths.hpp", 2, 1));
        NativeModule source = new(original.targetId, original.triple, original.pointerSize, original.types.Select(type => type.id.value == "char8"
            ? mutation == "wide" ? type with { size = 2 } : mutation == "signed" ? type with { isSigned = true } : type : type).ToImmutableArray(),
            mutation == "missing" ? original.declarations : original.declarations.Add(alias), [])
        { language = original.language };
        if (mutation == "missing") Assert.Null(StandardPathInputProbe.Find(source));
        else Assert.Throws<NotSupportedException>(() => StandardPathInputProbe.Find(source));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CountedUtf8PathCopiesActualChar8UnitsAndPreservesTheOriginalOpaqueExport(bool reference)
    {
        NativeBridgePlan primary = new NativeBridgePlanner().Plan(Context(reference), [new("check")]);
        NativeBridgePlan bridge = new SequenceInputAdapterPlanner().Apply(primary, [Request()]);
        Assert.All(primary.exports, original => Assert.Contains(original, bridge.exports));
        BridgeExport adapter = Assert.Single(bridge.exports, export => export.inputAdapterOf is not null);
        StandardSequenceArgument sequence = adapter.operation.argumentGroups[0].standardSequence!;
        Assert.Equal(StandardValueKind.Path, sequence.family);
        Assert.Equal(new StableId("char8"), sequence.elementType);
        Assert.Equal(EvidenceLevel.UserAsserted, sequence.evidence.level);
        Assert.Equal("uint8_t", bridge.types.Single(type => type.id == bridge.types.Single(pointer => pointer.id == adapter.parameters[0].type).elementType).cName);
        Assert.Throws<NotSupportedException>(() => new CppBridgeLanguageValidator().Validate(bridge, "c++17"));
        new CppBridgeLanguageValidator().Validate(bridge, "c++20");
        Sink sink = new();
        new TypedBridgeEmitter().Emit(bridge, sink, "paths");
        string source = sink.files["src/paths.cpp"];
        Assert.Contains("std::u8string bgcs_path_units_0", source);
        Assert.Contains("static_cast<char8_t>(arg0_data[", source);
        Assert.Contains("::std::filesystem::path>::value", source);
        Assert.DoesNotContain("reinterpret_cast<const char*>", source);
        Assert.DoesNotContain("bgcs_path_units_0.c_str()", source);
    }

    [Theory]
    [InlineData("no-assertion")]
    [InlineData("retained")]
    [InlineData("missing-char8")]
    [InlineData("wrong-width")]
    [InlineData("wrong-sign")]
    [InlineData("custom-path")]
    public void StandardPathInputCannotGuessItsEncodingOrApplyToArbitraryCustomPaths(string mutation)
    {
        LoweringContext context = Context(false);
        NativeModule source = context.module;
        if (mutation is "missing-char8" or "wrong-width" or "wrong-sign" or "custom-path")
        {
            context = context with
            {
                module = new(source.targetId, source.triple, source.pointerSize,
                source.types.Where(type => mutation != "missing-char8" || type.id != new StableId("char8")).Select(type => type.id == new StableId("char8")
                    ? mutation == "wrong-width" ? type with { size = 2 } : mutation == "wrong-sign" ? type with { isSigned = true } : type
                    : mutation == "custom-path" && type.id == new StableId("path") ? type with { spelling = "custom::Path" } : type).ToImmutableArray(),
                source.declarations, [])
                { language = source.language }
            };
        }
        CppFamilyBinding[] bindings = mutation == "custom-path" ? [new() { nativeType = "std::filesystem::path", family = StandardValueKind.Path,
            pathFormat = NativePathFormat.NativeUtf8, evidence = new(EvidenceLevel.UserAsserted,"custom-source-protocol") }] : [];
        NativeBridgePlan primary = new NativeBridgePlanner(familyBindings: bindings).Plan(context, [new("check")]);
        SequenceInputAdapterRequest request = mutation == "no-assertion" ? new("Check", [new("value")])
            : mutation == "retained" ? new("Check", [new("value") { retainsArgument = true, lifetime = NativeLifetimeKind.Call }]) : Request();
        Assert.Throws<NotSupportedException>(() => new SequenceInputAdapterPlanner().Apply(primary, [request]));
    }

    [Theory]
    [InlineData("unit")]
    [InlineData("evidence")]
    public void IndependentVerifierRejectsChangedPathInputFactsBeforeAnyWrite(string mutation)
    {
        NativeBridgePlan primary = new NativeBridgePlanner().Plan(Context(false), [new("check")]);
        NativeBridgePlan bridge = new SequenceInputAdapterPlanner().Apply(primary, [Request()]);
        BridgeExport adapter = Assert.Single(bridge.exports, export => export.inputAdapterOf is not null);
        BridgeArgumentGroup group = adapter.operation.argumentGroups[0];
        group = group with
        {
            standardSequence = mutation == "unit" ? group.standardSequence! with { elementType = new("int") }
            : group.standardSequence! with { evidence = new(EvidenceLevel.Declared, "invented-nonretention") }
        };
        BridgeExport changed = adapter with { operation = adapter.operation with { argumentGroups = [group] } };
        bridge = bridge with { exports = bridge.exports.Select(export => export.id == adapter.id ? changed : export).ToImmutableArray() };
        Sink sink = new();
        Assert.Throws<InvalidOperationException>(() => new TypedBridgeEmitter().Emit(bridge, sink, "paths"));
        Assert.Empty(sink.files);
    }

    private static SequenceInputAdapterRequest Request() => new("Check", [new("value") { retainsArgument = false, lifetime = NativeLifetimeKind.Call }]);
    private static LoweringContext Context(bool reference)
    {
        SourceLocation location = new("paths.hpp", 1, 1);
        NativeType unit = new(new("char8"), NativeTypeCategory.Primitive, "char8_t", 1, 1) { primitiveKind = "Char8", isSigned = false };
        NativeType integer = new(new("int"), NativeTypeCategory.Primitive, "int", 4, 4) { primitiveKind = "Int", isSigned = true };
        NativeType path = new(new("path"), NativeTypeCategory.Record, "std::filesystem::path", 32, 8) { declaration = new("owner") };
        NativeType qualified = new(new("const-path"), NativeTypeCategory.Qualified, "const std::filesystem::path", 32, 8) { elementType = path.id, qualifier = "Const" };
        NativeType pathReference = new(new("path-ref"), NativeTypeCategory.Reference, "const std::filesystem::path&", 8, 8) { elementType = qualified.id };
        NativeDeclaration owner = new(new("owner"), "Class", "path", path.spelling, path.id, location) { isCompleteDefinition = true, role = "Class" };
        NativeDeclaration parameter = new(new("value"), "Parameter", "value", "Check::value", reference ? pathReference.id : path.id, location) { parent = new("check") };
        NativeDeclaration function = new(new("check"), "Function", "Check", "Check", null, location) { returnType = integer.id, children = [parameter.id] };
        NativeModule module = new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8, [unit, integer, path, qualified, pathReference], [owner, parameter, function], []) { language = SourceLanguage.Cpp };
        return new(module, new(new(module.targetId), "windows", "x64", "msvc", module.triple, new()), "paths", ["paths.hpp"]);
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
