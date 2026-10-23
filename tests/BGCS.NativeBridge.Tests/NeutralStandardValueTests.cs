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
using Xunit;

namespace BGCS.NativeBridge.Tests;

public sealed class NeutralStandardValueTests
{
    [Theory]
    [InlineData("std::basic_string<char>", StandardValueKind.String, "char")]
    [InlineData("std::__cxx11::basic_string<char>", StandardValueKind.String, "char")]
    [InlineData("std::__1::vector<int>", StandardValueKind.Vector, "int")]
    [InlineData("std::vector<bool>", StandardValueKind.Vector, "bool")]
    [InlineData("std::span<int>", StandardValueKind.Span, "int")]
    [InlineData("std::array<int, 3>", StandardValueKind.Array, "int")]
    [InlineData("std::map<int, int>", StandardValueKind.Map, "int")]
    [InlineData("std::unordered_map<int, int>", StandardValueKind.Map, "int")]
    [InlineData("std::set<int>", StandardValueKind.Set, "int")]
    [InlineData("std::unordered_set<int>", StandardValueKind.Set, "int")]
    [InlineData("std::optional<int>", StandardValueKind.Optional, "int")]
    [InlineData("std::variant<int, int>", StandardValueKind.Variant, "int")]
    [InlineData("std::expected<int, int>", StandardValueKind.Expected, "int")]
    [InlineData("std::filesystem::path", StandardValueKind.Path, "int")]
    [InlineData("std::chrono::duration<int>", StandardValueKind.Duration, "int")]
    [InlineData("std::unique_ptr<int>", StandardValueKind.UniquePointer, "int")]
    [InlineData("std::shared_ptr<int>", StandardValueKind.SharedPointer, "int")]
    public void DetachedStandardFamilies_EmitFiniteCheckedAccessProtocols(
        string spelling,
        StandardValueKind family,
        string element
    ) {
        LoweringContext context = Context(spelling, element);
        NativeBridgePlan plan = new NativeBridgePlanner().Plan(context);
        Assert.Contains(plan.exports, export => export.operation.standardValue?.family == family);
        Sink sink = new();
        new TypedBridgeEmitter().Emit(plan, sink, "fixture");
        Assert.Equal(2, sink.files.Count);
        string source = sink.files["src/fixture.cpp"];
        Assert.Contains("catch (const std::exception&", source);
        if (family is StandardValueKind.String or StandardValueKind.Vector or StandardValueKind.Span or StandardValueKind.Array or StandardValueKind.Map or StandardValueKind.Set)
            Assert.Contains("throw std::out_of_range", source);
        if (spelling == "std::vector<bool>")
        {
            Assert.DoesNotContain("->data()", source);
            Assert.Contains("static_cast<uint8_t>((*bgcs_instance)[", source);
        }
        if (family == StandardValueKind.UniquePointer) Assert.Contains("std::move(*bgcs_instance)", source);
        if (family == StandardValueKind.Path) Assert.Contains("generic_u8string()", source);
    }

    [Theory]
    [InlineData("family")]
    [InlineData("arguments")]
    [InlineData("index")]
    [InlineData("result")]
    [InlineData("const")]
    [InlineData("release")]
    public void MutatedStandardOperation_IsRejectedBeforeEmission(string mutation)
    {
        NativeBridgePlan module = new NativeBridgePlanner().Plan(Context("std::vector<int>", "int"));
        BridgeExport selected = module.exports.Single(export => export.operation.standardValue?.action == StandardValueAction.GetAt);
        BridgeExport changed = mutation switch
        {
            "family" => selected with { operation = selected.operation with { standardValue = selected.operation.standardValue! with { family = StandardValueKind.Optional } } },
            "arguments" => selected with { operation = selected.operation with { standardValue = selected.operation.standardValue! with { argumentTypes = [] } } },
            "index" => selected with { parameters = selected.parameters.Select(parameter => parameter.name == "index" ? parameter with { nativeType = new StableId("integer") } : parameter).ToImmutableArray() },
            "result" => selected with { nativeResultType = null },
            "const" => selected with { operation = selected.operation with { standardValue = selected.operation.standardValue! with { action = StandardValueAction.SetAt } } },
            "release" => selected with { resultRelease = module.exports.First(export => export.operation.kind == BridgeOperationKind.Destroy).id },
            _ => throw new ArgumentException(nameof(mutation))
        };
        module = module with { exports = module.exports.Select(export => export.id == selected.id ? changed : export).ToImmutableArray() };
        Sink sink = new();
        Assert.Throws<InvalidOperationException>(() => new TypedBridgeEmitter().Emit(module, sink, "fixture"));
        Assert.Empty(sink.files);
    }

    [Fact]
    public void SimilarUserType_IsNotAStandardLibraryFamily()
    {
        LoweringContext context = Context("Example::vector<int>", "int");
        Assert.Null(new StandardLibraryFactReader().Read(context.module, new("record")));
    }

    [Fact]
    public void ByValueUniquePointer_MovesPayloadThroughItsExactCallerOwnedHolder()
    {
        LoweringContext context = Context("std::unique_ptr<int>", "int");
        NativeDeclaration consume = new(new("consume"), "Function", "Consume", "Example::Consume", null, new("fixture.hpp", 2, 1))
        { returnType = new("integer"), children = [new("value")] };
        NativeDeclaration value = new(new("value"), "Parameter", "value", "value", new("record"), consume.source) { parent = consume.id };
        NativeModule source = new(context.module.targetId, context.module.triple, context.module.pointerSize, context.module.types,
            [.. context.module.declarations, consume, value], [])
        { language = SourceLanguage.Cpp };
        NativeBridgePlan plan = new NativeBridgePlanner().Plan(context with { module = source });
        BridgeExport call = plan.exports.Single(export => export.operation.declaration == consume.id);
        Assert.Equal(BridgeConversionKind.Move, Assert.Single(call.parameters).conversion);
        Sink sink = new();
        new TypedBridgeEmitter().Emit(plan, sink, "fixture");
        Assert.Contains("std::move(*reinterpret_cast<::std::unique_ptr<int>*>(arg0))", sink.files["src/fixture.cpp"]);
        NativeBridgePlan mismatched = plan with
        {
            exports = plan.exports.Select(export => export.id == call.id
            ? export with { parameters = [export.parameters[0] with { nativeType = new("integer") }] } : export).ToImmutableArray()
        };
        Assert.Throws<InvalidOperationException>(() => new CppBridgeOperationVerifier().Verify(mismatched));
    }

    private static LoweringContext Context(
        string spelling,
        string element
    ) {
        SourceLocation location = new("fixture.hpp", 1, 1);
        NativeType integer = new(new("integer"), NativeTypeCategory.Primitive, element, element == "char" || element == "bool" ? 1 : 4, element == "char" || element == "bool" ? 1 : 4)
        { primitiveKind = element == "bool" ? "Bool" : element == "char" ? "Char" : "Int", isSigned = element != "bool" };
        NativeType record = new(new("record"), NativeTypeCategory.Record, spelling, 24, 8) { declaration = new("record-declaration") };
        ImmutableArray<NativeTemplateArgument> arguments = spelling.StartsWith("std::filesystem", StringComparison.Ordinal) ? []
            : spelling.Contains(", int>", StringComparison.Ordinal) ? [new("Type", element, integer.id), new("Type", element, integer.id)] : [new("Type", element, integer.id)];
        NativeDeclaration declaration = new(new("record-declaration"), "Class", spelling, spelling, record.id, location)
        { templateArguments = arguments, isCompleteDefinition = true, isSystemDeclaration = true };
        NativeDeclaration make = new(new("make"), "Function", "Make", "Example::Make", null, location) { returnType = record.id };
        NativeModule module = new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8, [integer, record], [declaration, make], []) { language = SourceLanguage.Cpp };
        NativeTargetDescriptor target = new(new(module.targetId), "windows", "x64", "msvc", module.triple, new());
        return new(module, target, "fixture", ["fixture.hpp"]);
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
