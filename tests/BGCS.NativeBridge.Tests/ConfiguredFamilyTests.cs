using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Analysis.Bridge;
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

public sealed class ConfiguredFamilyTests
{
    [Theory]
    [InlineData(StandardValueKind.String)]
    [InlineData(StandardValueKind.Vector)]
    [InlineData(StandardValueKind.Span)]
    [InlineData(StandardValueKind.Array)]
    [InlineData(StandardValueKind.Optional)]
    [InlineData(StandardValueKind.Map)]
    [InlineData(StandardValueKind.Set)]
    [InlineData(StandardValueKind.Expected)]
    public void ExactOptInUsesOpaqueActualTypeAndIndependentFiniteOperations(StandardValueKind family)
    {
        LoweringContext context = Context(family);
        CppFamilyBinding binding = Binding(family);
        Assert.Null(new StandardLibraryFactReader().Read(context.module, new("record")));
        NativeBridgePlan plan = new NativeBridgePlanner(familyBindings: [binding]).Plan(context);
        Assert.Single(plan.familyBindings);
        Assert.Contains(plan.types, type => type.kind == BridgeTypeKind.Handle && type.nativeType == new StableId("record"));
        Assert.DoesNotContain(plan.types, type => type.kind == BridgeTypeKind.Record && type.nativeType == new StableId("record"));
        Assert.Contains(plan.exports, export => export.operation.standardValue?.family == family);
        new CppBridgeOperationVerifier().Verify(plan);
        Sink sink = new();
        new TypedBridgeEmitter().Emit(plan, sink, "configured");
        string source = sink.files["src/configured.cpp"];
        Assert.Contains(family == StandardValueKind.Expected ? "BGCS configured family requires exact native expected value type" : "BGCS configured family requires exact native element type", source);
        Assert.Contains("#if defined(static_assert)", source);
        Assert.Contains("#if defined(Collection)", source);
        Assert.Contains("#if defined(custom)", source);
        Assert.Contains("::custom::Collection<", source);
        Assert.DoesNotContain("reinterpret_cast<::std::", source);
        if (family is StandardValueKind.Array or StandardValueKind.Span) Assert.Contains("contradicts its native fixed extent", source);
        if (family == StandardValueKind.Optional) Assert.Contains("if (!bgcs_instance->has_value())", source);
        if (family is StandardValueKind.Map or StandardValueKind.Set)
        {
            Assert.Contains("bgcs_iterator_end", source);
            Assert.Contains("configured associative count exceeds its iterator extent", source);
            Assert.Contains("matching native iterator sentinel", source);
        }
    }

    [Theory]
    [InlineData("name")]
    [InlineData("qualification")]
    [InlineData("duplicate")]
    [InlineData("evidence")]
    [InlineData("encoding")]
    [InlineData("unsupported")]
    public void InvalidOrAmbiguousConfigurationCannotClaimAFamily(string mutation)
    {
        CppFamilyBinding binding = Binding(StandardValueKind.Vector);
        ImmutableArray<CppFamilyBinding> bindings = mutation switch
        {
            "name" => [binding with { nativeTemplate = "custom::*" }],
            "qualification" => [binding with { nativeTemplate = "::::custom::Collection" }],
            "duplicate" => [binding, binding with { nativeTemplate = "::custom::Collection" }],
            "evidence" => [binding with { evidence = new(EvidenceLevel.Verified, "unsupported-claim") }],
            "encoding" => [binding with { stringEncoding = NativeTextEncoding.Utf8 }],
            "unsupported" => [binding with { family = (StandardValueKind)int.MaxValue }],
            _ => throw new ArgumentException(nameof(mutation))
        };
        Assert.ThrowsAny<Exception>(() => new NativeBridgePlanner(familyBindings: bindings));
    }

    [Theory]
    [InlineData("head")]
    [InlineData("arguments")]
    [InlineData("encoding")]
    [InlineData("standard")]
    public void MissingOrContradictoryNativeFactsAreRejected(string mutation)
    {
        LoweringContext context = Context(mutation == "encoding" ? StandardValueKind.String : StandardValueKind.Vector);
        CppFamilyBinding binding = Binding(mutation == "encoding" ? StandardValueKind.String : StandardValueKind.Vector);
        if (mutation == "head") binding = binding with { nativeTemplate = "custom::Missing" };
        if (mutation == "encoding") binding = binding with { stringEncoding = NativeTextEncoding.Utf16 };
        if (mutation is "arguments" or "standard")
        {
            NativeModule module = context.module;
            context = context with
            {
                module = new(module.targetId, module.triple, module.pointerSize,
                module.types.Select(type => mutation == "standard" && type.id == new StableId("record") ? type with { spelling = "std::vector<int>" } : type).ToImmutableArray(),
                module.declarations.Select(declaration => mutation == "arguments" && declaration.id == new StableId("owner") ? declaration with { templateArguments = [] } : declaration).ToImmutableArray(), [])
                { language = SourceLanguage.Cpp }
            };
            if (mutation == "standard") binding = binding with { nativeTemplate = "std::vector", family = StandardValueKind.Optional };
        }
        Assert.ThrowsAny<Exception>(() => new NativeBridgePlanner(familyBindings: [binding]).Plan(context));
    }

    [Theory]
    [InlineData("remove")]
    [InlineData("family")]
    [InlineData("type")]
    public void IndependentVerifierRejectsChangedFamilyOrSourceIdentity(string mutation)
    {
        NativeBridgePlan plan = new NativeBridgePlanner(familyBindings: [Binding(StandardValueKind.Vector)]).Plan(Context(StandardValueKind.Vector));
        plan = mutation switch
        {
            "remove" => plan with { familyBindings = [] },
            "family" => plan with { familyBindings = [Binding(StandardValueKind.Optional)] },
            "type" => plan with { types = plan.types.Select(type => type.kind == BridgeTypeKind.Handle ? type with { nativeType = new("element") } : type).ToImmutableArray() },
            _ => throw new ArgumentException(nameof(mutation))
        };
        Sink sink = new();
        Assert.ThrowsAny<Exception>(() => new TypedBridgeEmitter().Emit(plan, sink, "configured"));
        Assert.Empty(sink.files);
    }

    private static CppFamilyBinding Binding(StandardValueKind family) => new()
    {
        nativeTemplate = "custom::Collection",
        family = family,
        evidence = new(EvidenceLevel.UserAsserted, "fixture-user-protocol"),
        stringEncoding = family == StandardValueKind.String ? NativeTextEncoding.Utf8 : NativeTextEncoding.Unknown
    };

    internal static LoweringContext Context(StandardValueKind family)
    {
        SourceLocation source = new("custom.hpp", 1, 1);
        bool text = family == StandardValueKind.String;
        NativeType element = new(new("element"), NativeTypeCategory.Primitive, text ? "char" : "int", text ? 1 : 4, text ? 1 : 4)
        { primitiveKind = text ? "Char" : "Int", isSigned = true };
        string spelling = "custom::Collection<" + element.spelling + (family is StandardValueKind.Array or StandardValueKind.Span ? ", 3>"
            : family is StandardValueKind.Map or StandardValueKind.Expected ? ", " + element.spelling + ">" : ">");
        NativeType record = new(new("record"), NativeTypeCategory.Record, spelling, 24, 8) { declaration = new("owner") };
        ImmutableArray<NativeTemplateArgument> arguments = [new("Type", element.spelling, element.id)];
        if (family is StandardValueKind.Array or StandardValueKind.Span) arguments = arguments.Add(new("AsInteger", "3", null));
        if (family is StandardValueKind.Map or StandardValueKind.Expected) arguments = arguments.Add(new("Type", element.spelling, element.id));
        NativeDeclaration owner = new(new("owner"), "Class", "Collection", spelling, record.id, source)
        { role = "Struct", isCompleteDefinition = true, isStandardLayout = true, isTriviallyCopyable = true, isDefaultConstructible = true, templateArguments = arguments };
        NativeDeclaration make = new(new("make"), "Function", "Make", "Make", null, source) { returnType = record.id };
        NativeModule module = new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8, [element, record], [owner, make], []) { language = SourceLanguage.Cpp };
        NativeTargetDescriptor target = new(new(module.targetId), "windows", "x64", "msvc", module.triple, new());
        return new(module, target, "configured", ["custom.hpp"]);
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
