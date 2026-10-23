using System;
using System.Collections.Immutable;
using System.Text.Json;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Core.Targets;
using BGCS.NativeBridge.Emission;
using Xunit;

namespace BGCS.NativeBridge.Tests;

public sealed class NativeCallableAssertionFactsTests
{
    [Theory]
    [InlineData("Win64", "ms_abi", "sysv_abi")]
    [InlineData("X86_64SysV", "sysv_abi", "ms_abi")]
    public void ExplicitConventionFactsDetermineAssertionsWithoutInterpretingTargetNames(string convention, string expected, string unexpected)
    {
        NativeModule first = Module("private-target", "opaque-private-triple");
        NativeModule renamed = Module("different-target", "not-a-desktop-triple");
        NativeSourceConventions facts = new("", "", new(convention, false));
        string source = new NativeStorageAssertionEmitter().Emit(first, [], facts);
        Assert.Contains("__attribute__((" + expected + "))", source);
        Assert.DoesNotContain("__attribute__((" + unexpected + "))", source);
        Assert.Equal(source, new NativeStorageAssertionEmitter().Emit(renamed, [], facts));
    }

    [Fact]
    public void MissingOrUnsupportedCallingConventionFactsCannotBeAssumedFromTheTriple()
    {
        NativeModule module = Module("windows-x64-msvc", "x86_64-pc-windows-msvc");
        Assert.Throws<InvalidOperationException>(() => new NativeStorageAssertionEmitter().Emit(module, [], new("", "", null!)));
        Assert.Throws<NotSupportedException>(() => new NativeStorageAssertionEmitter().Emit(module, [], new("", "", new("unknown-call-abi", false))));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<NativeSourceConventions>("""
            {"exportDecoration":"","cdeclDecoration":""}
            """));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<NativeCallableAssertionFacts>("""
            {"defaultCConvention":"C"}
            """));
    }

    [Fact]
    public void AddresslessIntrinsicProofRequiresExplicitPermissionAndTheSameBoundedSignature()
    {
        NativeModule source = Module("private-target", "opaque-private-triple");
        NativeDeclaration function = source.declarations[0] with
        {
            returnType = new("void"),
            addressability = NativeCallableAddressability.DirectCallOnly,
            addressabilityEvidence = new(EvidenceLevel.Declared, "fixture:compiler-address-expression")
        };
        NativeModule intrinsic = Copy(source, [function]);
        Assert.Throws<NotSupportedException>(() => new NativeStorageAssertionEmitter().Emit(intrinsic, [], new("", "", new("Win64", false))));
        NativeSourceConventions permitted = new("", "", new("Win64", true));
        string proof = new NativeStorageAssertionEmitter().Emit(intrinsic, [], permitted);
        Assert.Contains("#pragma warning(error:4391 4392)", proof);
        Assert.Contains("extern void BGCS_VERIFY_CC_Win64 NativeAction(void);", proof);
        Assert.Throws<NotSupportedException>(() => new NativeStorageAssertionEmitter().Emit(Copy(source, [function with { returnType = new("int") }]), [], permitted));
        NativeDeclaration parameter = new(new("parameter"), "Parameter", "value", "NativeAction::value", new("int"), default) { parent = function.id };
        Assert.Throws<NotSupportedException>(() => new NativeStorageAssertionEmitter().Emit(Copy(source, [function with { children = [parameter.id] }, parameter]), [], permitted));
    }

    private static NativeModule Copy(NativeModule source, ImmutableArray<NativeDeclaration> declarations)
        => new(source.targetId, source.triple, source.pointerSize, source.types, declarations, []) { language = SourceLanguage.C };

    private static NativeModule Module(string targetId, string triple)
        => new(targetId, triple, 8,
            [new(new("int"), NativeTypeCategory.Primitive, "int", 4, 4) { primitiveKind = "Int", isSigned = true },
             new(new("void"), NativeTypeCategory.Primitive, "void", 0, null) { primitiveKind = "Void" }],
            [new(new("function"), "Function", "NativeAction", "NativeAction", null, default)
            { role = "Function", returnType = new("int"), callingConvention = "C", hasPrototype = true }], [])
            { language = SourceLanguage.C };
}
