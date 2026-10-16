using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.Cpp2C.Emission;
using BGCS.Cpp2C.Planning;
using Xunit;

namespace BGCS.Cpp2C.Tests;

public sealed class NeutralDtoStorageTests
{
    [Fact]
    public void PublicDtoStorage_PreservesStandaloneFieldsAndUsesPointerCopyChannels()
    {
        CppBridgeModule plan = Plan();
        Assert.Equal(2, plan.types.Count(type => type.kind == BridgeTypeKind.Record));
        Assert.DoesNotContain(plan.exports, export => export.operation.kind is BridgeOperationKind.DefaultConstruct or BridgeOperationKind.Construct or BridgeOperationKind.Destroy);
        BridgeExport transform = Assert.Single(plan.exports);
        BridgeParameter input = Assert.Single(transform.parameters);
        Assert.Equal(BridgeConversionKind.RecordCopy, input.conversion);
        Assert.Equal(BridgeConversionKind.RecordCopy, transform.resultConversion);
        Assert.Null(transform.resultRelease);
        BridgeType pointer = Assert.Single(plan.types, type => type.id == input.type);
        Assert.Equal(BridgeTypeKind.Pointer, pointer.kind);
        Assert.True(pointer.isConst);
        Sink sink = new();
        new TypedBridgeEmitter().Emit(plan, sink, "fixture");
        string source = sink.files["src/fixture.cpp"];
        Assert.Contains("std::bit_cast<::Sample::Packet>", source);
        Assert.Contains("std::memcpy(static_cast<void*>(bgcs_result)", source);
        Assert.Contains("A DTO boolean field must be zero or one", source);
        Assert.Contains("offsetof(::Sample::Packet, flags)", source);
        Assert.Contains("std::is_standard_layout<::Sample::Packet>::value", source);
        Assert.Contains(plan.languageRequirements, requirement => requirement.feature == "bridge.dto-copy" && requirement.minimumStandard == 20);
    }

    [Theory]
    [InlineData("c++11")]
    [InlineData("c++17")]
    [InlineData("gnu++17")]
    public void CopyingDtoValues_RequiresCxx20RatherThanSilentlyEmittingAnInvalidDialect(string standard)
    {
        NotSupportedException error = Assert.Throws<NotSupportedException>(() => new CppBridgeLanguageValidator().Validate(Plan(), standard));
        Assert.Contains("bridge.dto-copy", error.Message);
        Assert.Contains("C++20", error.Message);
    }

    [Theory]
    [InlineData("c++20")]
    [InlineData("c++23")]
    [InlineData("gnu++2a")]
    public void CopyingDtoValues_AcceptsSupportedExplicitDialects(string standard)
    {
        new CppBridgeLanguageValidator().Validate(Plan(), standard);
    }

    [Fact]
    public void StandaloneDtoStorage_DoesNotInventCxx20CopyHelpers()
    {
        CppBridgeModule storage = Plan() with { exports = [] };
        new CppBridgeLanguageValidator().Validate(storage, "c++11");
        Assert.DoesNotContain(storage.languageRequirements, requirement => requirement.minimumStandard > 11);
        Sink sink = new();
        new TypedBridgeEmitter().Emit(storage, sink, "fixture");
        string source = sink.files["src/fixture.cpp"];
        Assert.DoesNotContain("std::bit_cast", source);
        Assert.DoesNotContain("bgcs_input_", source);
        Assert.DoesNotContain("#include <bit>", source);
        Assert.Contains("std::is_trivially_copyable<::Sample::Packet>::value", source);
    }

    [Theory]
    [InlineData("field-offset")]
    [InlineData("missing-field")]
    [InlineData("array-length")]
    [InlineData("scalar-shape")]
    [InlineData("traits")]
    [InlineData("null-field")]
    public void InvalidDtoStorage_IsRejectedBeforeWritingAnyCandidate(string fault)
    {
        CppBridgeModule plan = Plan();
        BridgeType packet = plan.types.Single(type => type.kind == BridgeTypeKind.Record && type.nativeType == new StableId("packet"));
        if (fault is "field-offset" or "missing-field" or "null-field")
        {
            ImmutableArray<BridgeRecordField> fields = packet.recordStorage!.fields;
            fields = fault == "missing-field" ? fields.RemoveAt(0) : fault == "null-field" ? [null!]
                : fields.SetItem(0, fields[0] with { offset = 1 });
            plan = plan with { types = plan.types.Select(type => type.id == packet.id ? type with { recordStorage = packet.recordStorage with { fields = fields } } : type).ToImmutableArray() };
        }
        else if (fault == "array-length") plan = plan with { types = plan.types.Select(type => type.kind == BridgeTypeKind.FixedArray ? type with { arrayLength = 3 } : type).ToImmutableArray() };
        else if (fault == "scalar-shape") plan = plan with { types = plan.types.Select(type => type.cName == "int32_t" ? type with { cName = "float" } : type).ToImmutableArray() };
        else if (fault == "traits") plan = plan with { source = Source(false) };
        Sink sink = new();
        Assert.ThrowsAny<InvalidOperationException>(() => new TypedBridgeEmitter().Emit(plan, sink, "fixture"));
        Assert.Empty(sink.files);
    }

    [Fact]
    public void MissingCompilerTraits_KeepAnOpaqueProtocolInsteadOfInventingDtoFacts()
    {
        CppBridgeModule plan = Plan(null);
        Assert.DoesNotContain(plan.types, type => type.kind == BridgeTypeKind.Record);
        Assert.Contains(plan.types, type => type.kind == BridgeTypeKind.Handle);
        Assert.Equal(BridgeConversionKind.OwnedValue, plan.exports.Single(export => export.operation.declaration == new StableId("transform")).resultConversion);
    }

    [Fact]
    public void VoidPointerFieldsRetainCompletePointerStorageWithoutRequiringVoidValueStorage()
    {
        NativeModule source = Source(true);
        NativeType empty = new(new("native-void"), NativeTypeCategory.Primitive, "void", 0, null) { primitiveKind = "Void" };
        NativeType pointer = new(new("void-pointer"), NativeTypeCategory.Pointer, "void*", 8, 8) { elementType = empty.id };
        source = new(source.targetId, source.triple, source.pointerSize,
            source.types.Select(type => type.id == new StableId("standalone") ? type with { size = 8, alignment = 8 } : type).Append(empty).Append(pointer).ToImmutableArray(),
            source.declarations.Select(declaration => declaration.id == new StableId("number") ? declaration with { type = pointer.id } : declaration).ToImmutableArray(), source.diagnostics)
        { language = source.language, byteOrder = source.byteOrder };
        NativeTargetDescriptor target = new(new(source.targetId), "windows", "x64", "msvc", source.triple, new());
        CppBridgeModule plan = new CppBridgePlanner().Plan(new(source, target, "fixture", ["fixture.hpp"]));
        Sink sink = new();
        new TypedBridgeEmitter().Emit(plan, sink, "fixture");
        Assert.Matches(@"\bvoid\s+\*\s*number\s*;", sink.files["include/fixture.h"]);
    }

    [Fact]
    public void TrailingPrefixSeparator_DoesNotAllocateReservedCppIdentifiers()
    {
        NativeModule source = Source(true);
        NativeTargetDescriptor target = new(new(source.targetId), "windows", "x64", "msvc", source.triple, new());
        CppBridgeModule plan = new CppBridgePlanner().Plan(new(source, target, "Bimg_", ["fixture.hpp"]));
        Sink sink = new();
        new TypedBridgeEmitter().Emit(plan, sink, "Bimg_");
        Assert.All(plan.types.Where(type => type.cName.Length != 0), type => Assert.DoesNotContain("__", type.cName));
        Assert.All(plan.exports, export => Assert.DoesNotContain("__", export.name));
        Assert.Equal("Bimg_error", plan.errorTypeName);
        Assert.Contains("#define BIMG_API", sink.files["include/Bimg_.h"]);
        Assert.DoesNotContain("BIMG__API", sink.files["include/Bimg_.h"]);
    }

    private static CppBridgeModule Plan(bool? trivial = true)
    {
        NativeModule source = Source(trivial);
        NativeTargetDescriptor target = new(new(source.targetId), "windows", "x64", "msvc", source.triple, new());
        return new CppBridgePlanner().Plan(new(source, target, "fixture", ["fixture.hpp"]));
    }

    private static NativeModule Source(bool? trivial)
    {
        SourceLocation at = new("fixture.hpp", 1, 1);
        NativeType integer = new(new("integer"), NativeTypeCategory.Primitive, "int", 4, 4) { primitiveKind = "Int", isSigned = true };
        NativeType boolean = new(new("boolean"), NativeTypeCategory.Primitive, "bool", 1, 1) { primitiveKind = "Bool" };
        NativeType flags = new(new("flags-array"), NativeTypeCategory.Array, "bool[2]", 2, 1) { elementType = boolean.id, arrayLength = 2 };
        NativeType packet = new(new("packet"), NativeTypeCategory.Record, "Sample::Packet", 8, 4) { declaration = new("packet-decl") };
        NativeType standalone = new(new("standalone"), NativeTypeCategory.Record, "Sample::Standalone", 4, 4) { declaration = new("standalone-decl") };
        NativeDeclaration packetDecl = new(packet.declaration!.Value, "Class", "Packet", "Sample::Packet", packet.id, at)
        { role = "Struct", isCompleteDefinition = true, isStandardLayout = true, isTriviallyCopyable = trivial, children = [new("count"), new("flags")] };
        NativeDeclaration count = new(new("count"), "Field", "count", "Sample::Packet::count", integer.id, at) { parent = packetDecl.id, offset = 0 };
        NativeDeclaration bits = new(new("flags"), "Field", "flags", "Sample::Packet::flags", flags.id, at) { parent = packetDecl.id, offset = 4 };
        NativeDeclaration standaloneDecl = new(standalone.declaration!.Value, "Class", "Standalone", "Sample::Standalone", standalone.id, at)
        { role = "Struct", isCompleteDefinition = true, isStandardLayout = true, isTriviallyCopyable = trivial, children = [new("number")] };
        NativeDeclaration number = new(new("number"), "Field", "number", "Sample::Standalone::number", integer.id, at) { parent = standaloneDecl.id, offset = 0 };
        NativeDeclaration transform = new(new("transform"), "Function", "Transform", "Sample::Transform", null, at) { returnType = packet.id, children = [new("value")] };
        NativeDeclaration value = new(new("value"), "Parameter", "value", "value", packet.id, at) { parent = transform.id };
        return new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8, [integer, boolean, flags, packet, standalone],
            [packetDecl, count, bits, standaloneDecl, number, transform, value], []) { language = SourceLanguage.Cpp };
    }

    private sealed class Sink : IArtifactSink
    {
        internal Dictionary<string, string> files { get; } = [];
        public string WriteText(string relativePath, string content)
        {
            files.Add(relativePath, content);
            return relativePath;
        }
    }
}
