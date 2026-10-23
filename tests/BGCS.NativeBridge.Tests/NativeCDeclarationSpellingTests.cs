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
    public void CStorageUsesExactCompilerSpellingInsteadOfInventingATagFromItsDisplayName(NativeTypeCategory category, string spelling, string expected)
    {
        NativeModule module = Module(category, spelling);
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
    public void UnnameableCStorageNeedsATypedefOrAnExplicitLowering(NativeTypeCategory category, string spelling)
    {
        NotSupportedException failure = Assert.Throws<NotSupportedException>(() => new NativeTypeFormatter().FormatDeclaration(Module(category, spelling), new("storage"), "value"));
        Assert.Contains("compiler-established", failure.Message, StringComparison.Ordinal);
        Assert.Contains("typedef", failure.Message, StringComparison.Ordinal);
    }

    private static NativeModule Module(NativeTypeCategory category, string spelling)
    {
        NativeType integer = new(new("int"), NativeTypeCategory.Primitive, "int", 4, 4) { primitiveKind = "Int", isSigned = true };
        NativeType storage = new(new("storage"), category, spelling, 4, 4) { declaration = new("declaration"), elementType = category == NativeTypeCategory.Enumeration ? integer.id : null };
        NativeDeclaration declaration = new(new("declaration"), category == NativeTypeCategory.Enumeration ? "Enum" : "Class", "DisplayName", "DisplayName", storage.id, new("source.h", 1, 1))
        { role = spelling.StartsWith("union ", StringComparison.Ordinal) ? "Union" : "Struct", isCompleteDefinition = true };
        return new("fixture", "fixture-triple", 8, [integer, storage], [declaration], []) { language = SourceLanguage.C, byteOrder = NativeByteOrder.LittleEndian };
    }
}
