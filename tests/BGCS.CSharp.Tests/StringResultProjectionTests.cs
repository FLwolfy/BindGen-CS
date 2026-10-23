using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using BGCS.CSharp.Validation;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class StringResultProjectionTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, NativeTextEncoding.Utf8)]
    [InlineData(BindingImportMode.FunctionTable, NativeTextEncoding.Utf16)]
    [InlineData(BindingImportMode.FunctionTable, NativeTextEncoding.Utf32)]
    [InlineData(BindingImportMode.DllImport, NativeTextEncoding.Utf8)]
    [InlineData(BindingImportMode.DllImport, NativeTextEncoding.Utf16)]
    [InlineData(BindingImportMode.DllImport, NativeTextEncoding.Utf32)]
    [InlineData(BindingImportMode.LibraryImport, NativeTextEncoding.Utf8)]
    [InlineData(BindingImportMode.LibraryImport, NativeTextEncoding.Utf16)]
    [InlineData(BindingImportMode.LibraryImport, NativeTextEncoding.Utf32)]
    public async Task OwnedResultsProtectExactDecodeExtentNullAndReleaseAfterProcessing(BindingImportMode mode, NativeTextEncoding encoding)
    {
        ManagedApiModel model = Project(Module(encoding));
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native", sink) { importMode = mode };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        CSharpStringResultPlan result = Assert.Single(plan.stringResults);
        Assert.Equal("string", result.signature.returnType);
        Assert.Equal(model.interop.functions[0].id, result.signature.nativeExport);
        Assert.Equal(new[] { 0 }, result.signature.inputIndices);
        Assert.NotNull(result.releaseIndex);
        Assert.Empty(plan.directOwnedFactories);
        Assert.Empty(plan.ownedProjectionOmissions);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        var sources = sink.sources.Select(source => (Path: source.Key, Text: source.Value)).ToArray();
        new EmittedInteropContractReader().Verify(plan, sources, "native");
        foreach (string mutation in new[] { "lease", "cleanup", "codec", "cap", "null" })
        {
            int changes = 0;
            var altered = sources.Select(source =>
            {
                string changed = mutation switch
                {
                    "lease" => source.Text.Replace("this.m_session.AcquireCall()", "this.m_session.AcquireCleanupCall()", StringComparison.Ordinal),
                    "cleanup" => source.Text.Replace("finally", "catch", StringComparison.Ordinal),
                    "codec" => source.Text.Replace("Codec.Decode(", "Codec.DecodeLength(", StringComparison.Ordinal),
                    "cap" => source.Text.Replace(", 1048576", ", 2", StringComparison.Ordinal),
                    _ => source.Text.Replace("== null", "!= null", StringComparison.Ordinal)
                };
                if (changed != source.Text) changes++;
                return (source.Path, changed);
            }).ToArray();
            Assert.True(changes > 0, mutation);
            Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan, altered, "native"));
        }
    }

    [Theory]
    [InlineData("unknown-encoding")]
    [InlineData("wrong-width")]
    [InlineData("unknown-byte-order")]
    [InlineData("unknown-null")]
    [InlineData("missing-extent")]
    [InlineData("wrong-units")]
    [InlineData("inferred-evidence")]
    [InlineData("missing-release")]
    [InlineData("wrong-release-type")]
    [InlineData("wrong-release-domain")]
    [InlineData("thread-affine")]
    [InlineData("borrowed-call")]
    [InlineData("borrowed-owner")]
    [InlineData("conflicting-extent")]
    [InlineData("partial-code-unit")]
    [InlineData("excessive-copy")]
    [InlineData("unknown-output-retention")]
    public void UnprovenStringProtocolKeepsRawAbiAndOneConcreteOmission(string fault)
    {
        InteropModule original = Module(NativeTextEncoding.Utf16);
        InteropFunction getter = original.functions[0];
        OwnershipContract owned = getter.returnOwnership!;
        StringContract text = getter.returnString!;
        text = fault switch
        {
            "unknown-encoding" => text with { encoding = NativeTextEncoding.Unknown },
            "wrong-width" => text with { codeUnitWidth = 4 },
            "unknown-byte-order" => text with { byteOrder = NativeTextByteOrder.Unknown },
            "unknown-null" => text with { allowsNull = null },
            "missing-extent" => text with { zeroTerminated = null },
            "wrong-units" => text with { lengthUnit = NativeLengthUnit.UnicodeScalars },
            "inferred-evidence" => text with { evidence = new(EvidenceLevel.Inferred, "guess") },
            "conflicting-extent" => text with { fixedLength = 2 },
            "partial-code-unit" => text with { zeroTerminated = false, fixedLength = 3, lengthUnit = NativeLengthUnit.Bytes },
            "excessive-copy" => text with { zeroTerminated = false, fixedLength = int.MaxValue, maximumCodeUnits = int.MaxValue },
            "unknown-output-retention" => text with { zeroTerminated = false, lengthParameter = getter.parameters[0].id, lengthDirection = NativeParameterDirection.Output },
            _ => text
        };
        owned = fault switch
        {
            "missing-release" => owned with { releaseFunction = null },
            "wrong-release-domain" => owned with { allocator = null },
            "thread-affine" => owned with { releaseThreadDomain = "unknown" },
            "borrowed-call" => new(NativeOwnershipKind.Borrowed, owned.lifetime with { kind = NativeLifetimeKind.Call, owner = null }, owned.evidence),
            "borrowed-owner" => new(NativeOwnershipKind.Borrowed, owned.lifetime, owned.evidence),
            _ => owned
        };
        getter = getter with { returnOwnership = owned, returnString = text with { ownership = owned, lifetime = owned.lifetime } };
        var functions = original.functions.SetItem(0, getter);
        if (fault == "wrong-release-type") functions = functions.SetItem(1, functions[1] with { returnType = new("integer") });
        InteropModule module = new(original.libraryId, original.targetId, original.pointerSize, original.types, functions, []) { byteOrder = original.byteOrder };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(Project(module), new("native", new NeutralBackendTests.MemorySink()));
        Assert.Empty(plan.stringResults);
        Assert.Equal(getter.id, Assert.Single(plan.ownedProjectionOmissions).nativeExport);
        Assert.Contains(plan.methods, method => method.nativeExport == getter.id);
        Assert.Empty(plan.directOwnedFactories);
        Assert.Empty(plan.directBorrowedFactories);
    }

    [Fact]
    public void ModuleBorrowingCopiesWithoutInventingAnOwnerOrReleaseAndKeepsNullableSignature()
    {
        InteropModule original = Module(NativeTextEncoding.Utf8);
        InteropFunction getter = original.functions[0];
        OwnershipContract borrowed = new(NativeOwnershipKind.Borrowed,
            new(NativeLifetimeKind.Module, null, getter.returnOwnership!.evidence), getter.returnOwnership.evidence);
        getter = getter with { returnOwnership = borrowed, returnString = getter.returnString! with
        { ownership = borrowed, lifetime = borrowed.lifetime, allowsNull = true, zeroTerminated = false, fixedLength = 3 } };
        InteropModule module = new(original.libraryId, original.targetId, original.pointerSize, original.types, original.functions.SetItem(0, getter), []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(Project(module), new("native", new NeutralBackendTests.MemorySink()));
        CSharpStringResultPlan result = Assert.Single(plan.stringResults);
        Assert.Null(result.releaseIndex);
        Assert.Equal(3, result.exactCodeUnits);
        Assert.Equal("string?", result.signature.returnType);
        Assert.Empty(plan.directBorrowedFactories);
        Assert.Empty(plan.ownedProjectionOmissions);
    }

    private static ManagedApiModel Project(InteropModule module) => new DefaultManagedProjection().Project(module, new("Generated.TextResult", "Api"), CancellationToken.None);
    private static InteropModule Module(NativeTextEncoding encoding)
    {
        Evidence evidence = new(EvidenceLevel.UserAsserted, "fixture:string-result");
        int width = encoding == NativeTextEncoding.Utf8 ? 1 : encoding == NativeTextEncoding.Utf16 ? 2 : 4;
        InteropType empty = new(new("void"), "void", NativeTypeCategory.Primitive, 0, 1) { primitiveKind = "Void" };
        InteropType integer = new(new("integer"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropType unit = new(new("unit"), "unit", NativeTypeCategory.Primitive, width, width)
        { primitiveKind = width == 1 ? "UnsignedChar" : width == 2 ? "UnsignedShort" : "UnsignedInt", isSigned = false };
        InteropType pointer = new(new("pointer"), "unit*", NativeTypeCategory.Pointer, 8, 8) { elementType = unit.id };
        OwnershipContract owned = new(NativeOwnershipKind.Owned, new(NativeLifetimeKind.Owner, new("get"), evidence), evidence)
        { releaseFunction = new("release"), allocator = new("explicit-domain"), releaseThreadDomain = "any" };
        StringContract text = new(encoding, width, NativeLengthUnit.CodeUnits, NativeParameterDirection.Output, owned.lifetime, evidence)
        { byteOrder = NativeTextByteOrder.Native, allowsNull = false, zeroTerminated = true, ownership = owned };
        InteropFunction getter = new(new("get"), "get", pointer.id, [new(new("input"), "input", integer.id)], "C")
        { returnOwnership = owned, returnString = text };
        InteropFunction release = new(new("release"), "release", empty.id, [new(new("release-value"), "value", pointer.id)], "C");
        return new("text-results", "windows-x64-msvc", 8, [empty, integer, unit, pointer], [getter, release], []) { byteOrder = NativeByteOrder.LittleEndian };
    }
}
