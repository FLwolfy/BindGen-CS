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

public sealed class ConfiguredCStrFamilyTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CStrNeedsOnlyItsActualPointerMemberAndPreservesReceiverConstness(bool isConst)
    {
        LoweringContext context = Context(isConst);
        StandardLibraryFactReader reader = new([Binding()]);
        Assert.Equal(new StableId("char"), reader.Read(context.module, new("text"))!.argumentTypes[0]);
        Assert.Equal(isConst, reader.ReadCStr(context.module, new("text")).isConst);
        NativeBridgePlan bridge = new NativeBridgePlanner(familyBindings: [Binding()]).Plan(context, [new("make")]);
        Assert.DoesNotContain(bridge.exports, export => export.operation.standardValue?.action is StandardValueAction.Construct or StandardValueAction.SetAt or StandardValueAction.Append);
        Assert.All(bridge.exports.Where(export => export.operation.standardValue is not null), export =>
            Assert.Equal(isConst, bridge.types.Single(type => type.id == export.parameters[0].type).isConst));
        Sink sink = new();
        new TypedBridgeEmitter().Emit(bridge, sink, "minimal");
        string text = sink.files["src/minimal.cpp"];
        Assert.Contains("bgcs_cstr_count < 64ULL", text);
        Assert.Contains("c_str has no terminator", text);
        Assert.DoesNotContain("bgcs_instance->size()", text);
        Assert.DoesNotContain("::value_type", text);
        Assert.DoesNotContain("bgcs_instance->push_back", text);
    }

    [Theory]
    [InlineData("missing-lifetime")]
    [InlineData("unknown-termination")]
    [InlineData("owner-lifetime")]
    [InlineData("zero-bound")]
    [InlineData("wrong-encoding")]
    [InlineData("unrelated-contract")]
    [InlineData("inferred-copy")]
    public void ReadabilityAndConstructorClaimsCannotBeInferredFromPointerWidth(string mutation)
    {
        CppFamilyBinding binding = Binding();
        binding = mutation switch
        {
            "missing-lifetime" => binding with { cStr = binding.cStr! with { readableLifetime = new(NativeLifetimeKind.Unknown, null, User) } },
            "unknown-termination" => binding with { cStr = binding.cStr! with { nullTerminated = new(EvidenceLevel.Unknown, "unproved") } },
            "owner-lifetime" => binding with { cStr = binding.cStr! with { readableLifetime = new(NativeLifetimeKind.Call, new("owner"), User) } },
            "zero-bound" => binding with { cStr = binding.cStr! with { maximumCodeUnits = 0 } },
            "wrong-encoding" => binding with { stringEncoding = NativeTextEncoding.Utf16 },
            "unrelated-contract" => binding with { stringAccess = CppStringAccess.Indexed },
            "inferred-copy" => binding with { cStr = binding.cStr! with { constructorCopiesInput = new(EvidenceLevel.Inferred, "guess") } },
            _ => throw new ArgumentException(nameof(mutation))
        };
        Assert.Throws<ArgumentException>(() => new NativeBridgePlanner(familyBindings: [binding]));
    }

    [Theory]
    [InlineData("pointer")]
    [InlineData("private")]
    [InlineData("variadic")]
    [InlineData("ambiguous")]
    public void CStrMustBeAnAccessibleUnambiguousFixedNativeMember(string mutation)
    {
        LoweringContext context = Context(true);
        NativeModule source = context.module;
        ImmutableArray<NativeDeclaration> declarations = source.declarations.Select(declaration => declaration.id == new StableId("getter")
            ? mutation switch
            {
                "pointer" => declaration with { returnType = new("char") },
                "private" => declaration with { visibility = "Private" },
                "variadic" => declaration with { isVariadic = true },
                _ => declaration
            }
            : mutation == "ambiguous" && declaration.id == new StableId("owner") ? declaration with { children = [new("getter"), new("getter2")] } : declaration).ToImmutableArray();
        if (mutation == "ambiguous") declarations = declarations.Add(source.GetDeclaration(new("getter")) with { id = new("getter2") });
        NativeModule changed = new(source.targetId, source.triple, source.pointerSize, source.types, declarations, []);
        Assert.Throws<NotSupportedException>(() => new StandardLibraryFactReader([Binding()]).Read(changed, new("text")));
    }

    [Theory]
    [InlineData(StandardValueAction.SetAt)]
    [InlineData(StandardValueAction.Append)]
    public void ReadOnlyCStrProtocolCannotBeMutatedByAnExtension(StandardValueAction action)
    {
        NativeBridgePlan bridge = new NativeBridgePlanner(familyBindings: [Binding()]).Plan(Context(true), [new("make")]);
        BridgeExport getter = bridge.exports.Single(export => export.operation.standardValue?.action == StandardValueAction.GetAt);
        BridgeExport changed = getter with { operation = getter.operation with { standardValue = getter.operation.standardValue! with { action = action } } };
        Assert.Throws<InvalidOperationException>(() => new StandardValueOperationVerifier().Verify(bridge, changed));
    }

    [Fact]
    public void NativePathUsesItsDataAndSizeWithoutInventingValueTypeOrIndexMembers()
    {
        CppFamilyBinding path = new() { nativeType = "custom::Text", family = StandardValueKind.Path, pathFormat = NativePathFormat.NativeUtf8, evidence = User };
        NativeBridgePlan bridge = new NativeBridgePlanner(familyBindings: [path]).Plan(Context(true), [new("make")]);
        Sink sink = new();
        new TypedBridgeEmitter().Emit(bridge, sink, "minimal");
        Assert.Contains("bgcs_instance->u8string()", sink.files["src/minimal.cpp"]);
        Assert.Contains("bgcs_path_utf8.data()[", sink.files["src/minimal.cpp"]);
        Assert.DoesNotContain("generic_u8string()", sink.files["src/minimal.cpp"]);
        Assert.DoesNotContain("::value_type", sink.files["src/minimal.cpp"]);
    }

    private static readonly Evidence User = new(EvidenceLevel.UserAsserted, "minimal-explicit-protocol");
    private static CppFamilyBinding Binding() => new()
    {
        nativeType = "custom::Text",
        family = StandardValueKind.String,
        stringEncoding = NativeTextEncoding.Utf8,
        stringAccess = CppStringAccess.CStrNullTerminated,
        evidence = User,
        cStr = new() { maximumCodeUnits = 64, readableLifetime = new(NativeLifetimeKind.Call, null, User), nullTerminated = User }
    };

    private static LoweringContext Context(bool isConst)
    {
        SourceLocation source = new("minimal.hpp", 1, 1);
        NativeType unit = new(new("char"), NativeTypeCategory.Primitive, "char", 1, 1) { primitiveKind = "Char", isSigned = true };
        NativeType qualified = new(new("const-char"), NativeTypeCategory.Qualified, "const char", 1, 1) { elementType = unit.id, qualifier = "Const" };
        NativeType pointer = new(new("units"), NativeTypeCategory.Pointer, "const char*", 8, 8) { elementType = qualified.id };
        NativeType text = new(new("text"), NativeTypeCategory.Record, "custom::Text", 24, 8) { declaration = new("owner") };
        NativeDeclaration owner = new(new("owner"), "Class", "Text", "custom::Text", text.id, source)
        { role = "Class", isCompleteDefinition = true, isDefaultConstructible = false, children = [new("getter")] };
        NativeDeclaration getter = new(new("getter"), "Function", "c_str", "custom::Text::c_str", null, source)
        { parent = owner.id, visibility = "Public", isConst = isConst, returnType = pointer.id };
        NativeDeclaration make = new(new("make"), "Function", "Make", "Make", null, source) { returnType = text.id };
        NativeModule module = new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8, [unit, qualified, pointer, text], [owner, getter, make], []) { language = SourceLanguage.Cpp };
        return new(module, new(new(module.targetId), "windows", "x64", "msvc", module.triple, new()), "minimal", ["minimal.hpp"]);
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
