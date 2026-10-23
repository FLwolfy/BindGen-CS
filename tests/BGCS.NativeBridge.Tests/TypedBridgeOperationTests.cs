using System;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Core.Artifacts;
using BGCS.Core.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.NativeBridge.Emission;
using Xunit;

namespace BGCS.NativeBridge.Tests;

public sealed class TypedBridgeOperationTests
{
    [Fact]
    public void CompleteOwnedConstruction_EmitsBothCandidates()
    {
        Sink sink = new();
        Assert.Equal(2, new TypedBridgeEmitter().Emit(Module(), sink, "fixture").Length);
        Assert.Equal(2, sink.writes);
    }

    [Theory]
    [InlineData("wrong-owner")]
    [InlineData("missing-release")]
    [InlineData("wrong-release")]
    [InlineData("discarded-result")]
    [InlineData("borrowed-construction")]
    [InlineData("unknown-operation")]
    [InlineData("wrong-instance-const")]
    [InlineData("input-owned-conversion")]
    [InlineData("pointer-cycle")]
    [InlineData("duplicate-export")]
    public void InvalidPlans_FailBeforeWritingAnyArtifact(string mutation)
    {
        NativeBridgePlan module = Module();
        BridgeExport construct = module.exports[0];
        BridgeExport release = module.exports[1];
        module = mutation switch
        {
            "wrong-owner" => module with { exports = [construct with { operation = construct.operation with { ownerType = new StableId("other-native") } }, release] },
            "missing-release" => module with { exports = [construct with { resultRelease = null }, release] },
            "wrong-release" => module with { exports = [construct with { resultRelease = construct.id }, release] },
            "discarded-result" => module with { exports = [construct with { resultType = null, nativeResultType = null, resultRelease = null }, release] },
            "borrowed-construction" => module with { exports = [construct with { resultConversion = BridgeConversionKind.PointerCast, resultRelease = null }, release] },
            "unknown-operation" => module with { exports = [construct with { operation = construct.operation with { kind = (BridgeOperationKind)999 } }, release] },
            "wrong-instance-const" => module with { types = module.types.Select(type => type.id == new StableId("object-pointer") ? type with { isConst = true } : type).ToImmutableArray() },
            "input-owned-conversion" => module with { exports = [construct, release with { parameters = [release.parameters[0] with { conversion = BridgeConversionKind.OwnedValue }] }] },
            "pointer-cycle" => module with { types = module.types.Select(type => type.id == new StableId("object-pointer") ? type with { elementType = type.id } : type).ToImmutableArray() },
            "duplicate-export" => module with { exports = [construct, release with { id = construct.id }] },
            _ => throw new ArgumentException(nameof(mutation))
        };
        Sink sink = new();
        Assert.ThrowsAny<InvalidOperationException>(() => new TypedBridgeEmitter().Emit(module, sink, "fixture"));
        Assert.Equal(0, sink.writes);
    }

    private static NativeBridgePlan Module()
    {
        StableId ownerId = new("owner"), recordId = new("owner-native"), ctorId = new("ctor"), dtorId = new("dtor");
        SourceLocation location = new("fixture.hpp", 1, 1);
        NativeType record = new(recordId, NativeTypeCategory.Record, "Widget", 4, 4) { declaration = ownerId };
        NativeType other = new(new("other-native"), NativeTypeCategory.Record, "Other", 4, 4) { declaration = new("other") };
        NativeDeclaration owner = new(ownerId, "Class", "Widget", "Widget", recordId, location) { children = [ctorId, dtorId] };
        NativeDeclaration ctor = new(ctorId, "Function", "Widget", "Widget::Widget", null, location) { parent = ownerId, role = "Constructor" };
        NativeDeclaration dtor = new(dtorId, "Function", "~Widget", "Widget::~Widget", null, location) { parent = ownerId, role = "Destructor" };
        NativeDeclaration otherDeclaration = new(new("other"), "Class", "Other", "Other", other.id, location);
        NativeModule native = new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8, [record, other], [owner, ctor, dtor, otherDeclaration], []) { language = SourceLanguage.Cpp };
        BridgeType handle = new(new("object"), BridgeTypeKind.Handle, "fixture_widget") { nativeType = recordId };
        BridgeType pointer = new(new("object-pointer"), BridgeTypeKind.Pointer, string.Empty) { elementType = handle.id };
        BridgeParameter self = new(new("self"), "bgcs_self", pointer.id, recordId, BridgeConversionKind.PointerCast);
        BridgeExport release = new(new("release"), "fixture_release", [self], new(BridgeOperationKind.Destroy, dtorId, recordId, self.id));
        BridgeExport construct = new(new("construct"), "fixture_construct", [], new(BridgeOperationKind.Construct, ctorId, recordId, null))
        { resultType = pointer.id, nativeResultType = recordId, resultConversion = BridgeConversionKind.OwnedValue, resultRelease = release.id };
        return new(native, "fixture", ["fixture.hpp"], [handle, pointer], [construct, release]);
    }

    private sealed class Sink : IArtifactSink
    {
        public int writes { get; private set; }
        public string WriteText(
            string relativePath,
            string content
        ) { writes++; return relativePath; }
    }
}
