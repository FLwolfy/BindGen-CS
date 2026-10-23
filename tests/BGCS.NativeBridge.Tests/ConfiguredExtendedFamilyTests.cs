using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Analysis.Bridge;
using BGCS.Core.Artifacts;
using BGCS.Core.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.NativeBridge.Emission;
using BGCS.NativeBridge.Planning;
using Xunit;

namespace BGCS.NativeBridge.Tests;

public sealed class ConfiguredExtendedFamilyTests
{
    [Theory]
    [InlineData(StandardValueKind.UniquePointer)]
    [InlineData(StandardValueKind.SharedPointer)]
    [InlineData(StandardValueKind.Variant)]
    [InlineData(StandardValueKind.Path)]
    [InlineData(StandardValueKind.Duration)]
    [InlineData(StandardValueKind.TimePoint)]
    public void ExactOwnerVariantPathAndChronoProtocolsRetainActualNativeTypes(StandardValueKind family)
    {
        (LoweringContext context, ImmutableArray<CppFamilyBinding> bindings) = Context(family);
        NativeBridgePlan bridge = new NativeBridgePlanner(familyBindings: bindings).Plan(context);
        new CppBridgeOperationVerifier().Verify(bridge);
        Sink sink = new();
        new TypedBridgeEmitter().Emit(bridge, sink, "extended");
        string source = sink.sources["src/extended.cpp"];
        Assert.Contains("using bgcs_family_proof_", source);
        Assert.DoesNotContain("reinterpret_cast<::std::", source);
        Assert.All(bridge.exports.Where(export => export.resultRelease is not null), export => Assert.Null(export.resultOwnership));
        if (family is StandardValueKind.UniquePointer or StandardValueKind.SharedPointer)
        {
            Assert.Contains("exact borrowed smart-owner pointer", source);
            Assert.Contains("nonthrowing smart-owner cleanup", source);
            Assert.Contains(family == StandardValueKind.UniquePointer ? "move-only nonthrowing unique-owner transfer" : "nonthrowing shared-owner copy", source);
            Assert.Contains(bridge.exports, export => export.operation.standardValue?.action == StandardValueAction.GetPointer && export.resultBorrowsInstance);
        }
        if (family == StandardValueKind.Variant)
        {
            Assert.Contains("exact configured variant alternative 1", source);
            Assert.Contains("configured variant alternative is inactive", source);
            Assert.Contains("bgcs_instance->template get<1>()", source);
            Assert.DoesNotContain("std::get<", source);
        }
        if (family == StandardValueKind.Path)
        {
            Assert.Contains("one-byte UTF-8 path code units", source);
            Assert.Contains("generic_u8string()", source);
            Assert.DoesNotContain("GetAt", string.Join(',', bridge.exports.Select(export => export.operation.standardValue?.action.ToString())));
        }
        if (family is StandardValueKind.Duration or StandardValueKind.TimePoint)
        {
            NativeDurationPeriod period = new StandardLibraryFactReader(bindings).ReadPeriod(context.module, new("record"))!;
            Assert.Equal(1, period.numerator);
            Assert.Equal(1000, period.denominator);
            Assert.Contains("exact normalized native duration ratio", source);
            Assert.DoesNotContain("duration_cast", source);
            if (family == StandardValueKind.TimePoint) Assert.Contains("exact native clock identity", source);
        }
    }

    [Theory]
    [InlineData("variant-missing")]
    [InlineData("variant-unknown")]
    [InlineData("variant-unrelated")]
    [InlineData("path-missing")]
    [InlineData("path-unknown")]
    [InlineData("path-unrelated")]
    public void ProtocolSelectorsMustBeExplicitAndBelongToTheSelectedFamily(string mutation)
    {
        CppFamilyBinding binding = Binding(mutation.StartsWith("path", StringComparison.Ordinal) ? StandardValueKind.Path : StandardValueKind.Variant);
        binding = mutation switch
        {
            "variant-missing" => binding with { variantAccess = CppVariantAccess.None },
            "variant-unknown" => binding with { variantAccess = (CppVariantAccess)99 },
            "variant-unrelated" => binding with { family = StandardValueKind.Vector },
            "path-missing" => binding with { pathFormat = null },
            "path-unknown" => binding with { pathFormat = (NativePathFormat)99 },
            "path-unrelated" => binding with { family = StandardValueKind.Vector },
            _ => throw new ArgumentException(nameof(mutation))
        };
        Assert.Throws<ArgumentException>(() => new NativeBridgePlanner(familyBindings: [binding]));
    }

    [Theory]
    [InlineData(StandardValueKind.Map)]
    [InlineData(StandardValueKind.Set)]
    [InlineData(StandardValueKind.Expected)]
    [InlineData(StandardValueKind.UniquePointer)]
    [InlineData(StandardValueKind.SharedPointer)]
    [InlineData(StandardValueKind.Variant)]
    public void ValueChannelsCannotBeInventedForVoid(StandardValueKind family)
    {
        (LoweringContext context, ImmutableArray<CppFamilyBinding> bindings) = Context(family);
        NativeModule module = context.module;
        context = context with { module = new(module.targetId, module.triple, module.pointerSize,
            module.types.Select(type => type.id == new StableId("element") ? type with { spelling = "void", primitiveKind = "Void", size = null, alignment = null, isSigned = null } : type).ToImmutableArray(),
            module.declarations, []) { language = module.language } };
        Assert.Throws<NotSupportedException>(() => new NativeBridgePlanner(familyBindings: bindings).Plan(context));
    }

    [Theory]
    [InlineData("period")]
    [InlineData("recursive-time-point")]
    [InlineData("variant-access")]
    public void IndependentBridgeVerificationRejectsChangedProtocolFactsBeforeWrites(string mutation)
    {
        StandardValueKind family = mutation == "variant-access" ? StandardValueKind.Variant : mutation == "period" ? StandardValueKind.Duration : StandardValueKind.TimePoint;
        (LoweringContext context, ImmutableArray<CppFamilyBinding> bindings) = Context(family);
        NativeBridgePlan bridge = new NativeBridgePlanner(familyBindings: bindings).Plan(context);
        if (mutation == "variant-access") bridge = bridge with { familyBindings = [bindings[0] with { variantAccess = CppVariantAccess.None }] };
        else
        {
            NativeModule module = bridge.source;
            bridge = bridge with { source = new(module.targetId, module.triple, module.pointerSize, module.types,
                module.declarations.Select(declaration => mutation == "period" && declaration.id == new StableId("ratio-declaration")
                    ? declaration with { templateArguments = [new("Integral", "0", null), new("Integral", "1", null)] }
                    : mutation == "recursive-time-point" && declaration.id == new StableId("owner")
                        ? declaration with { templateArguments = declaration.templateArguments.SetItem(1, new("Type", "recursive", new("record"))) } : declaration).ToImmutableArray(), [])
                { language = module.language } };
        }
        Sink sink = new();
        Assert.ThrowsAny<Exception>(() => new TypedBridgeEmitter().Emit(bridge, sink, "extended"));
        Assert.Empty(sink.sources);
    }

    private static CppFamilyBinding Binding(StandardValueKind family, string head = "custom::Collection") => new()
    {
        nativeTemplate = head, family = family, evidence = new(EvidenceLevel.UserAsserted, "explicit-custom-value-protocol"),
        variantAccess = family == StandardValueKind.Variant ? CppVariantAccess.MemberGet : CppVariantAccess.None,
        pathFormat = family == StandardValueKind.Path ? NativePathFormat.GenericUtf8 : null
    };

    private static (LoweringContext context, ImmutableArray<CppFamilyBinding> bindings) Context(StandardValueKind family)
    {
        LoweringContext context = ConfiguredFamilyTests.Context(family);
        if (family is not (StandardValueKind.Duration or StandardValueKind.TimePoint or StandardValueKind.Variant)) return (context, [Binding(family)]);
        NativeModule original = context.module;
        List<NativeType> types = original.types.ToList();
        List<NativeDeclaration> declarations = original.declarations.ToList();
        NativeDeclaration owner = declarations.Single(declaration => declaration.id == new StableId("owner"));
        SourceLocation source = owner.source;
        if (family == StandardValueKind.Variant)
        {
            NativeType second = new(new("second"), NativeTypeCategory.Primitive, "double", 8, 8) { primitiveKind = "Double", isSigned = true };
            types.Add(second);
            owner = owner with { qualifiedName = "custom::Collection<int, double>", templateArguments = owner.templateArguments.Add(new("Type", "double", second.id)) };
            types[types.FindIndex(type => type.id == new StableId("record"))] = types.Single(type => type.id == new StableId("record")) with { spelling = owner.qualifiedName };
        }
        else
        {
            NativeType ratio = new(new("ratio"), NativeTypeCategory.Record, "std::ratio<2, 2000>", 1, 1) { declaration = new("ratio-declaration") };
            types.Add(ratio);
            declarations.Add(new(new("ratio-declaration"), "Class", "ratio", ratio.spelling, ratio.id, source)
            { role = "Struct", isCompleteDefinition = true, templateArguments = [new("Integral", "2", null), new("Integral", "2000", null)] });
            if (family == StandardValueKind.Duration)
            {
                owner = owner with { qualifiedName = "custom::Collection<int, std::ratio<2, 2000>>", templateArguments = owner.templateArguments.Add(new("Type", ratio.spelling, ratio.id)) };
                types[types.FindIndex(type => type.id == new StableId("record"))] = types.Single(type => type.id == new StableId("record")) with { spelling = owner.qualifiedName };
            }
            else
            {
                NativeType clock = new(new("clock"), NativeTypeCategory.Record, "custom::Clock", 1, 1) { declaration = new("clock-declaration") };
                NativeType duration = new(new("duration"), NativeTypeCategory.Record, "custom::Ticks<int, std::ratio<2, 2000>>", 4, 4) { declaration = new("duration-declaration") };
                types.AddRange([clock, duration]);
                declarations.Add(new(new("clock-declaration"), "Class", "Clock", clock.spelling, clock.id, source) { role = "Struct", isCompleteDefinition = true });
                declarations.Add(new(new("duration-declaration"), "Class", "Ticks", duration.spelling, duration.id, source)
                { role = "Struct", isCompleteDefinition = true, templateArguments = [new("Type", "int", new("element")), new("Type", ratio.spelling, ratio.id)] });
                owner = owner with { qualifiedName = "custom::Collection<custom::Clock, custom::Ticks<int, std::ratio<2, 2000>>>",
                    templateArguments = [new("Type", clock.spelling, clock.id), new("Type", duration.spelling, duration.id)] };
                types[types.FindIndex(type => type.id == new StableId("record"))] = types.Single(type => type.id == new StableId("record")) with { spelling = owner.qualifiedName };
            }
        }
        declarations[declarations.FindIndex(declaration => declaration.id == owner.id)] = owner;
        context = context with { module = new(original.targetId, original.triple, original.pointerSize, types.ToImmutableArray(), declarations.ToImmutableArray(), []) { language = original.language } };
        return (context, family == StandardValueKind.TimePoint ? [Binding(family), Binding(StandardValueKind.Duration, "custom::Ticks")] : [Binding(family)]);
    }

    private sealed class Sink : IArtifactSink
    {
        internal Dictionary<string, string> sources { get; } = [];
        public string WriteText(string relativePath, string content) { sources.Add(relativePath, content); return relativePath; }
    }
}
