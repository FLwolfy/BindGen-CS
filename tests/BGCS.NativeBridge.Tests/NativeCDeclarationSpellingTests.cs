using System;
using BGCS.Analysis.Types;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using Xunit;

namespace BGCS.NativeBridge.Tests;

public sealed class NativeCDeclarationSpellingTests
{
    [Theory]
    [InlineData(NativeTypeCategory.Enumeration, "AnonymousEnumAlias", "AnonymousEnumAlias value")]
    [InlineData(NativeTypeCategory.Enumeration, "enum ActualEnumTag", "enum ActualEnumTag value")]
    [InlineData(NativeTypeCategory.Record, "AnonymousRecordAlias", "AnonymousRecordAlias value")]
    [InlineData(NativeTypeCategory.Record, "struct ActualRecordTag", "struct ActualRecordTag value")]
    [InlineData(NativeTypeCategory.Record, "union ActualUnionTag", "union ActualUnionTag value")]
    [InlineData(NativeTypeCategory.Enumeration, "選択", "選択 value")]
    [InlineData(NativeTypeCategory.Record, "struct 印", "struct 印 value")]
    [InlineData(NativeTypeCategory.Record, "𐐀Value", "𐐀Value value")]
    [InlineData(NativeTypeCategory.Enumeration, "enum 𐐀Choice", "enum 𐐀Choice value")]
    [InlineData(NativeTypeCategory.Record, "ⅣValue", "ⅣValue value")]
    [InlineData(NativeTypeCategory.Record, "A\u0301Value", "A\u0301Value value")]
    [InlineData(NativeTypeCategory.Record, "A\u203F1", "A\u203F1 value")]
    public void CStorageUsesExactCompilerSpellingInsteadOfInventingATagFromItsDisplayName(
        NativeTypeCategory category,
        string spelling,
        string expected
    ) {
        NativeModule module = Module(category, spelling, expected[..^" value".Length]);
        Assert.Equal(expected, new NativeTypeFormatter().FormatDeclaration(module, new("storage"), "value"));
    }

    [Theory]
    [InlineData(NativeTypeCategory.Enumeration, "enum (anonymous at source.h:1:1)")]
    [InlineData(NativeTypeCategory.Record, "struct (unnamed at source.h:1:1)")]
    [InlineData(NativeTypeCategory.Record, "Record; injected")]
    [InlineData(NativeTypeCategory.Record, "1Named")]
    [InlineData(NativeTypeCategory.Record, "\u0301Value")]
    [InlineData(NativeTypeCategory.Record, "😀")]
    [InlineData(NativeTypeCategory.Record, "Bad\uD800Name")]
    [InlineData(NativeTypeCategory.Record, "LooksLikeAValidAliasButHasNoDeclaration")]
    public void UnnameableCStorageNeedsATypedefOrAnExplicitLowering(
        NativeTypeCategory category,
        string spelling
    ) {
        NotSupportedException failure = Assert.Throws<NotSupportedException>(() => new NativeTypeFormatter().FormatDeclaration(Module(category, spelling), new("storage"), "value"));
        Assert.Contains("compiler-established", failure.Message, StringComparison.Ordinal);
        Assert.Contains("typedef", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("(unnamed at source.h:1:1)", "struct ActualRecordTag")]
    [InlineData("union Owner::(anonymous at source.h:2:3)", "ActualAlias")]
    public void ARealSourceDeclarationRemainsUsableWhenTheDiagnosticSpellingIsNotASourceName(
        string diagnostic,
        string sourceName
    ) {
        Assert.Equal(sourceName + " value", new NativeTypeFormatter().FormatDeclaration(Module(NativeTypeCategory.Record, diagnostic, sourceName), new("storage"), "value"));
    }

    [Theory]
    [InlineData(SourceLanguage.C)]
    [InlineData(SourceLanguage.Cpp)]
    public void AQualifiedAliasCannotInventAnUnqualifiedAnonymousRecordName(SourceLanguage language)
    {
        NativeType storage = new(new("storage"), NativeTypeCategory.Record, "(unnamed at source.h:1:1)", 4, 4) { declaration = new("record") };
        NativeType qualified = new(new("qualified"), NativeTypeCategory.Qualified, "const storage", 4, 4) { elementType = storage.id, qualifier = "Const" };
        NativeType alias = new(new("alias"), NativeTypeCategory.Alias, "ConstAlias", 4, 4) { elementType = qualified.id, declaration = new("typedef") };
        NativeDeclaration record = new(new("record"), "Class", "", "", storage.id, new("source.h", 1, 1)) { role = "Struct", isCompleteDefinition = true };
        NativeDeclaration typedef = new(new("typedef"), "Typedef", "ConstAlias", "ConstAlias", alias.id, new("source.h", 1, 1));
        NativeModule module = new("fixture", "fixture-triple", 8, [storage, qualified, alias], [record, typedef], []) { language = language };
        NativeTypeFormatter formatter = new();
        Assert.Throws<NotSupportedException>(() => formatter.FormatDeclaration(module, storage.id, "value"));
        Assert.Equal("ConstAlias value", formatter.FormatDeclaration(module, alias.id, "value"));
    }

    private static NativeModule Module(
        NativeTypeCategory category,
        string spelling,
        string? sourceName = null
    ) {
        NativeType integer = new(new("int"), NativeTypeCategory.Primitive, "int", 4, 4) { primitiveKind = "Int", isSigned = true };
        NativeType storage = new(new("storage"), category, spelling, 4, 4) { declaration = new("declaration"), elementType = category == NativeTypeCategory.Enumeration ? integer.id : null };
        string[] words = sourceName?.Split(' ', 2) ?? [];
        string tag = words.Length == 2 ? words[1] : string.Empty;
        NativeDeclaration declaration = new(new("declaration"), category == NativeTypeCategory.Enumeration ? "Enum" : "Class", tag, tag, storage.id, new("source.h", 1, 1))
        { role = words.Length == 2 && words[0] == "union" ? "Union" : "Struct", isCompleteDefinition = true };
        if (words.Length == 1)
        {
            NativeType alias = new(new("alias"), NativeTypeCategory.Alias, words[0], 4, 4) { elementType = storage.id, declaration = new("alias-declaration") };
            NativeDeclaration typedef = new(new("alias-declaration"), "Typedef", words[0], words[0], alias.id, new("source.h", 1, 1));
            return new("fixture", "fixture-triple", 8, [integer, storage, alias], [declaration, typedef], []) { language = SourceLanguage.C, byteOrder = NativeByteOrder.LittleEndian };
        }
        return new("fixture", "fixture-triple", 8, [integer, storage], [declaration], []) { language = SourceLanguage.C, byteOrder = NativeByteOrder.LittleEndian };
    }
}
