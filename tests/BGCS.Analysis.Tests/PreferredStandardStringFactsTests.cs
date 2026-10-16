using System;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Analysis.Bridge;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Native;
using Xunit;

namespace BGCS.Analysis.Tests;

public sealed class PreferredStandardStringFactsTests
{
    [Theory]
    [InlineData(null, NativeTextEncoding.Utf8, 1)]
    [InlineData("__1", NativeTextEncoding.Utf8, 1)]
    [InlineData("__cxx11", NativeTextEncoding.Utf8, 1)]
    [InlineData("__1", NativeTextEncoding.Utf16, 2)]
    [InlineData("__1", NativeTextEncoding.Utf32, 4)]
    public void PreferredAliasRetainsActualStandardTemplateAndCodeUnitIdentity(string? abiNamespace, NativeTextEncoding encoding, int width)
    {
        NativeModule module = Module(abiNamespace, width);
        CppFamilyBinding binding = Binding("Text", encoding);
        StandardLibraryFactReader configured = new([binding]);

        configured.ValidateBindings(module);

        Assert.Equal(binding, configured.FindBinding(module, new("alias")));
        StandardValueOperation operation = Assert.IsType<StandardValueOperation>(configured.Read(module, new("alias")));
        Assert.Equal(StandardValueKind.String, operation.family);
        Assert.Equal(new StableId("unit"), Assert.Single(operation.argumentTypes));
        StandardValueOperation builtin = Assert.IsType<StandardValueOperation>(new StandardLibraryFactReader().Read(module, new("record")));
        Assert.Equal(operation.family, builtin.family);
        Assert.Equal(operation.argumentTypes.ToArray(), builtin.argumentTypes.ToArray());
        Assert.Equal("std::string", module.GetType(new("record")).spelling);
        Assert.Equal("basic_string", module.GetDeclaration(new("record-decl")).name);
        Assert.Equal(new StableId("record"), StandardLibraryFactReader.Resolve(module, new("alias")).id);
    }

    [Fact]
    public void PreferredAliasesWithDifferentAllocatorArgumentsRemainDistinctCanonicalRecords()
    {
        NativeModule original = Module("__1", 1);
        NativeType record = original.GetType(new("record"));
        NativeDeclaration declaration = original.GetDeclaration(new("record-decl"));
        NativeType other = record with { id = new("other-record"), spelling = "std::pmr::string", declaration = new("other-record-decl") };
        NativeType alias = new(new("other-alias"), NativeTypeCategory.Alias, "OtherText", 24, 8)
        { elementType = other.id, canonicalType = other.id };
        NativeDeclaration otherDeclaration = declaration with
        {
            id = other.declaration!.Value, type = other.id, qualifiedName = other.spelling,
            templateArguments = declaration.templateArguments.SetItem(2, new("AsType", "std::polymorphic_allocator<char>", new("other-allocator")))
        };
        NativeModule module = Copy(original,
            original.types.Add(other).Add(alias).Add(new(new("other-allocator"), NativeTypeCategory.Record, "std::polymorphic_allocator<char>", 1, 1)),
            original.declarations.Add(otherDeclaration));
        CppFamilyBinding ordinary = Binding("Text", NativeTextEncoding.Utf8), alternate = Binding("OtherText", NativeTextEncoding.Utf8);
        StandardLibraryFactReader reader = new([ordinary, alternate]);

        reader.ValidateBindings(module);

        Assert.Equal(ordinary, reader.FindBinding(module, new("alias")));
        Assert.Equal(alternate, reader.FindBinding(module, alias.id));
        Assert.NotEqual(StandardLibraryFactReader.Resolve(module, new("alias")).id, StandardLibraryFactReader.Resolve(module, alias.id).id);
        Assert.NotEqual(declaration.templateArguments[2].type, otherDeclaration.templateArguments[2].type);
        Assert.Equal(new StableId("unit"), Assert.Single(reader.Read(module, alias.id)!.argumentTypes));
    }

    [Fact]
    public void TwoConfiguredAliasesOfOnePreferredSpecializationRemainAmbiguous()
    {
        NativeModule original = Module("__1", 1);
        NativeType alias = original.GetType(new("alias")) with { id = new("second-alias"), spelling = "OtherText" };
        NativeModule module = Copy(original, original.types.Add(alias), original.declarations);

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => new StandardLibraryFactReader(
            [Binding("Text", NativeTextEncoding.Utf8), Binding("OtherText", NativeTextEncoding.Utf8)]).ValidateBindings(module));

        Assert.Contains("same canonical native class", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("foreign-namespace")]
    [InlineData("unknown-abi-namespace")]
    [InlineData("nested-standard-namespace")]
    [InlineData("class-parent")]
    [InlineData("different-template")]
    [InlineData("primary-template")]
    [InlineData("missing-template-type")]
    [InlineData("wrong-declared-type")]
    [InlineData("incomplete")]
    public void AliasSpellingCannotSubstituteForStandardDeclarationAndTemplateFacts(string defect)
    {
        NativeModule original = Module("__1", 1);
        NativeDeclaration owner = original.GetDeclaration(new("record-decl"));
        ImmutableArray<NativeDeclaration> declarations = original.declarations;
        switch (defect)
        {
            case "foreign-namespace": declarations = Replace(declarations, "std", item => item with { name = "foreign", qualifiedName = "foreign" }); break;
            case "unknown-abi-namespace": declarations = Replace(declarations, "abi", item => item with { name = "unknown_abi", qualifiedName = "std::unknown_abi" }); break;
            case "nested-standard-namespace": declarations = Replace(declarations, "std", item => item with { parent = new("outer") }).Add(new(new("outer"), "Namespace", "foreign", "foreign", null, At)); break;
            case "class-parent": declarations = Replace(declarations, "abi", item => item with { kind = "Class" }); break;
            case "different-template": owner = owner with { name = "different_string" }; break;
            case "primary-template": owner = owner with { templateKind = "TemplateClass", templateParameters = [new("unit")] }; break;
            case "missing-template-type": owner = owner with { templateArguments = owner.templateArguments.SetItem(2, new("AsType", "unresolved", null)) }; break;
            case "wrong-declared-type": owner = owner with { type = new("allocator") }; break;
            case "incomplete": owner = owner with { isCompleteDefinition = false }; break;
        }
        declarations = Replace(declarations, "record-decl", _ => owner);
        NativeModule module = Copy(original, original.types, declarations);

        Assert.Null(new StandardLibraryFactReader().Read(module, new("alias")));
        Assert.Throws<NotSupportedException>(() => new StandardLibraryFactReader([Binding("Text", NativeTextEncoding.Utf8)]).ValidateBindings(module));
    }

    [Fact]
    public void PreferredAliasStillRejectsAnEncodingThatContradictsNativeCodeUnitWidth()
    {
        NativeModule module = Module("__1", 2);

        NotSupportedException failure = Assert.Throws<NotSupportedException>(() => new StandardLibraryFactReader(
            [Binding("Text", NativeTextEncoding.Utf8)]).ValidateBindings(module));

        Assert.Contains("code-unit storage", failure.Message, StringComparison.Ordinal);
    }

    private static readonly SourceLocation At = new("preferred.hpp", 1, 1);

    private static CppFamilyBinding Binding(string name, NativeTextEncoding encoding) => new()
    { nativeType = name, family = StandardValueKind.String, stringEncoding = encoding, evidence = new(EvidenceLevel.UserAsserted, "declared-encoding") };

    private static NativeModule Module(string? abiNamespace, int width)
    {
        NativeType unit = new(new("unit"), NativeTypeCategory.Primitive, width == 1 ? "char" : "char" + (width * 8) + "_t", width, width)
        { primitiveKind = width == 1 ? "Char" : width == 2 ? "Char16" : "Char32", isSigned = false };
        NativeType record = new(new("record"), NativeTypeCategory.Record, "std::string", 24, 8) { declaration = new("record-decl") };
        NativeType alias = new(new("alias"), NativeTypeCategory.Alias, "Text", 24, 8) { elementType = record.id, canonicalType = record.id };
        NativeDeclaration standard = new(new("std"), "Namespace", "std", "std", null, At);
        NativeDeclaration owner = new(new("record-decl"), "Class", "basic_string", "std::string", record.id, At)
        {
            parent = new(abiNamespace is null ? "std" : "abi"), isCompleteDefinition = true, templateKind = "TemplateSpecializedClass",
            templateArguments = [new("AsType", unit.spelling, unit.id), new("AsType", "std::char_traits", new("traits")), new("AsType", "std::allocator", new("allocator"))]
        };
        ImmutableArray<NativeDeclaration> declarations = [standard, owner];
        if (abiNamespace is not null) declarations = declarations.Add(new(new("abi"), "Namespace", abiNamespace, "std::" + abiNamespace, null, At) { parent = standard.id });
        return new("synthetic", "x86_64-unknown-linux-gnu", 8,
            [unit, record, alias, new(new("traits"), NativeTypeCategory.Record, "std::char_traits", 1, 1), new(new("allocator"), NativeTypeCategory.Record, "std::allocator", 1, 1)], declarations, []);
    }

    private static ImmutableArray<NativeDeclaration> Replace(ImmutableArray<NativeDeclaration> declarations, string id, Func<NativeDeclaration, NativeDeclaration> update)
        => declarations.Select(item => item.id.value == id ? update(item) : item).ToImmutableArray();

    private static NativeModule Copy(NativeModule original, ImmutableArray<NativeType> types, ImmutableArray<NativeDeclaration> declarations)
        => new(original.targetId, original.triple, original.pointerSize, types, declarations, original.diagnostics);
}
