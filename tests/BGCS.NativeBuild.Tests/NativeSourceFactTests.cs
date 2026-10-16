using System;
using System.Collections.Immutable;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.NativeBuild.Validation;
using Xunit;

namespace BGCS.NativeBuild.Tests;

public sealed class NativeSourceFactTests
{
    [Theory]
    [InlineData("constructor")]
    [InlineData("private-field")]
    [InlineData("anonymous-field")]
    [InlineData("bitfield")]
    [InlineData("virtual-order")]
    [InlineData("virtual-nothrow")]
    [InlineData("virtual-nothrow-unknown")]
    [InlineData("virtual-volatile")]
    [InlineData("virtual-reference")]
    [InlineData("parameter-array-bound")]
    [InlineData("inline")]
    [InlineData("definition")]
    [InlineData("native-linkage")]
    [InlineData("addressability")]
    [InlineData("enum-value")]
    public void SameSizeDeclarationsRequireExactEffectiveFacts(string difference)
    {
        NativeModule original = Module(difference, changed: false);
        NativeModule native = Module(difference, changed: true);
        Assert.True(NativeSourceFactComparer.RequiresVerification(original));
        new NativeSourceFactComparer().Verify(original, Module(difference, changed: false, prefix: "fresh:"));
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => new NativeSourceFactComparer().Verify(original, native));
        Assert.Contains("Native effective source facts differ", failure.Message);
    }

    private static NativeModule Module(string difference, bool changed, string prefix = "")
    {
        StableId integer = new(prefix + "int"), scalar = new(prefix + "scalar"), recordType = new(prefix + "record-type"), owner = new(prefix + "owner");
        StableId field = new(prefix + "field"), ctor = new(prefix + "ctor"), parameter = new(prefix + "parameter"), virtualOne = new(prefix + "virtual-one"), virtualTwo = new(prefix + "virtual-two");
        StableId enumeration = new(prefix + "enum"), enumType = new(prefix + "enum-type"), enumValue = new(prefix + "enum-value");
        StableId pointer = new(prefix + "pointer"), array = new(prefix + "array");
        SourceLocation source = new("source.hpp", 1, 1);
        NativeType[] types = [new(integer, NativeTypeCategory.Primitive, "int", 4, 4) { primitiveKind = "Int", isSigned = true },
            new(scalar, NativeTypeCategory.Primitive, changed && difference is "constructor" or "private-field" or "anonymous-field" ? "float" : "int", 4, 4)
            { primitiveKind = changed && difference is "constructor" or "private-field" or "anonymous-field" ? "Float" : "Int", isSigned = changed && difference is "constructor" or "private-field" or "anonymous-field" ? null : true },
            new(recordType, NativeTypeCategory.Record, "Sample", 4, 4) { declaration = owner },
            new(enumType, NativeTypeCategory.Enumeration, "Choice", 4, 4) { declaration = enumeration, elementType = integer },
            new(pointer, NativeTypeCategory.Pointer, "int*", 8, 8) { elementType = integer },
            new(array, NativeTypeCategory.Array, "int[]", changed ? 16 : 12, 4) { elementType = integer, arrayLength = changed ? 4 : 3 }];
        NativeDeclaration record = new(owner, "Class", "Sample", "Sample", recordType, source)
        {
            role = "Class", isCompleteDefinition = true, isDefinition = true, isStandardLayout = true, isTriviallyCopyable = true,
            children = changed && difference == "virtual-order" ? [field, ctor, virtualTwo, virtualOne] : [field, ctor, virtualOne, virtualTwo]
        };
        NativeDeclaration[] declarations = [record,
            new(field, "Field", "value", "Sample::value", difference == "private-field" ? scalar : integer, source)
            { parent = owner, visibility = difference == "private-field" ? "Private" : "Public", offset = 0, bitOffset = 0,
                bitWidth = difference == "bitfield" ? changed ? 2 : 1 : null },
            new(ctor, "Function", "Sample", "Sample::Sample", null, source) { parent = owner, role = "Constructor", visibility = "Public", children = [parameter], callingConvention = "C", returnType = integer },
            new(parameter, "Parameter", "input", "Sample::Sample::input", difference == "parameter-array-bound" ? pointer : scalar, source)
            { parent = ctor, sourceType = difference == "parameter-array-bound" ? array : null },
            new(virtualOne, "Function", "First", "Sample::First", null, source)
            {
                parent = owner, role = "Function", visibility = "Public", isVirtual = true, callingConvention = "C", returnType = integer,
                isNoThrow = difference == "virtual-nothrow-unknown" && changed ? null : difference == "virtual-nothrow" && changed,
                isVolatile = difference == "virtual-volatile" && changed,
                referenceQualifier = difference == "virtual-reference" ? changed ? NativeReferenceQualifier.RValue : NativeReferenceQualifier.LValue : NativeReferenceQualifier.None,
                isInline = difference == "inline" && changed, isDefinition = difference == "definition" && changed,
                nativeLinkage = difference == "native-linkage" && changed ? NativeLinkageKind.Internal : NativeLinkageKind.External,
                addressability = difference == "addressability" && changed ? NativeCallableAddressability.DirectCallOnly : NativeCallableAddressability.Addressable
            },
            new(virtualTwo, "Function", "Second", "Sample::Second", null, source) { parent = owner, role = "Function", visibility = "Public", isVirtual = true, callingConvention = "C", returnType = integer },
            new(enumeration, "Enum", "Choice", "Choice", enumType, source) { children = [enumValue] },
            new(enumValue, "EnumItem", "Selected", "Choice::Selected", null, source) { parent = enumeration, evaluatedIntegralValue = changed && difference == "enum-value" ? "2" : "1" }];
        ImmutableArray<NativeType> typeFacts = types.ToImmutableArray();
        ImmutableArray<NativeDeclaration> declarationFacts = declarations.ToImmutableArray();
        if (difference == "anonymous-field")
        {
            StableId anonymous = new(prefix + "anonymous"), anonymousType = new(prefix + "anonymous-type"), nestedField = new(prefix + "nested-field");
            typeFacts = typeFacts.Add(new(anonymousType, NativeTypeCategory.Record, "anonymous at source.hpp", 4, 4) { declaration = anonymous });
            declarationFacts = declarationFacts.SetItem(1, declarationFacts[1] with { type = anonymousType }).AddRange(ImmutableArray.Create(
                new NativeDeclaration(anonymous, "Class", "", "", anonymousType, source) { parent = owner, role = "Struct", isCompleteDefinition = true, children = [nestedField] },
                new NativeDeclaration(nestedField, "Field", "nested", "nested", scalar, source) { parent = anonymous, visibility = "Public", offset = 0, bitOffset = 0 }));
        }
        return new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8, typeFacts, declarationFacts, [])
        { language = SourceLanguage.Cpp, byteOrder = NativeByteOrder.LittleEndian };
    }
}
