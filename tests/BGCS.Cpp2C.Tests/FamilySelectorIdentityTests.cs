using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using BGCS.Analysis.Declarations;
using BGCS.Analysis.Interop;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.Contracts.Requests;
using Xunit;

namespace BGCS.Cpp2C.Tests;

public sealed class FamilySelectorIdentityTests
{
    private static readonly string s_root = Path.Combine(Path.GetTempPath(), "bgcs-family-selector-identity");
    private static readonly string s_header = Path.Combine(s_root, "native.h");

    [Theory]
    [InlineData(NativeDeclarationCategory.Records, "SharedRecord", "record", false)]
    [InlineData(NativeDeclarationCategory.Records, "SharedRecord", "record", true)]
    [InlineData(NativeDeclarationCategory.Enumerations, "SharedEnum", "enum", false)]
    [InlineData(NativeDeclarationCategory.Enumerations, "SharedEnum", "enum", true)]
    public void TypedNamesResolveTagsBeforeTheirSameNamedTypedefs(NativeDeclarationCategory family, string name, string id, bool excluded)
    {
        DeclarationCategorySelection rule = excluded ? new(family) { excludeDeclarations = [name] } : new(family) { includeDeclarations = [name] };
        DeclarationSelection selected = Select(Source(), new() { includeCategories = [family], categories = [rule] });
        if (excluded)
            Assert.Empty(selected.publicDeclarations);
        else
            Assert.Equal(new StableId(id), Assert.Single(selected.publicDeclarations));
        Assert.Empty(new NativeModuleVerifier().Validate(selected.module));
    }

    [Theory]
    [InlineData(NativeDeclarationCategory.Records, "alias-record")]
    [InlineData(NativeDeclarationCategory.Enumerations, "alias-enum")]
    public void ExactIdsDoNotRebindToANameInTheRequestedFamily(NativeDeclarationCategory family, string wrongId)
    {
        ArgumentException failure = Assert.Throws<ArgumentException>(() => Select(Source(), new()
        { categories = [new(family) { includeDeclarations = [wrongId] }] }));
        Assert.Contains("selected a different declaration family", failure.Message);
    }

    [Theory]
    [InlineData("SharedRecord")]
    [InlineData("SharedEnum")]
    public void GlobalNamesRemainAmbiguousAcrossNativeFamilies(string name)
    {
        ArgumentException failure = Assert.Throws<ArgumentException>(() => Select(Source(), new() { includeDeclarations = [name] }));
        Assert.Contains("ambiguous across 2 declarations", failure.Message);
    }

    [Theory]
    [InlineData(NativeDeclarationCategory.Records, "SharedRecord", "record")]
    [InlineData(NativeDeclarationCategory.Enumerations, "SharedEnum", "enum")]
    public void TwoMatchesInsideOneFamilyRemainAmbiguous(NativeDeclarationCategory family, string name, string id)
    {
        NativeModule module = Source();
        NativeDeclaration original = module.GetDeclaration(new(id));
        NativeDeclaration duplicate = original with { id = new(id + "-duplicate"), type = null, children = [] };
        NativeModule ambiguous = new(module.targetId, module.triple, module.pointerSize, module.types, module.declarations.Add(duplicate), [])
        { language = module.language, byteOrder = module.byteOrder };
        ArgumentException failure = Assert.Throws<ArgumentException>(() => Select(ambiguous, new()
        { categories = [new(family) { includeDeclarations = [name] }] }));
        Assert.Contains("ambiguous across 2 declarations", failure.Message);
    }

    [Theory]
    [InlineData(NativeDeclarationCategory.Records, "SharedEnum")]
    [InlineData(NativeDeclarationCategory.Enumerations, "SharedRecord")]
    public void NamesFromOtherFamiliesStillFailExplicitly(NativeDeclarationCategory family, string name)
    {
        ArgumentException failure = Assert.Throws<ArgumentException>(() => Select(Source(), new()
        { categories = [new(family) { includeDeclarations = [name] }] }));
        Assert.Contains("selected a different declaration family", failure.Message);
    }

    [Theory]
    [InlineData(NativeDeclarationCategory.Records, "SharedRecord")]
    [InlineData(NativeDeclarationCategory.Enumerations, "SharedEnum")]
    public void ImplicitCoNamedTypedefCannotRestoreATagOutsideItsFamilyAllowlist(NativeDeclarationCategory family, string name)
    {
        NativeModule module = WithOtherTag(family);
        DeclarationSelection selected = Select(module, new() { categories = [new(family) { includeDeclarations = [name] }] });
        Assert.DoesNotContain(selected.module.declarations, declaration => declaration.id.value is "other" or "other-alias");
        Assert.Contains(selected.publicDeclarations, id => id.value == (family == NativeDeclarationCategory.Records ? "record" : "enum"));
        Assert.Contains(selected.publicDeclarations, id => id.value == (family == NativeDeclarationCategory.Records ? "enum" : "record"));
        Assert.Empty(new NativeModuleVerifier().Validate(selected.module));
    }

    [Theory]
    [InlineData(NativeDeclarationCategory.Records, "record", "enum")]
    [InlineData(NativeDeclarationCategory.Enumerations, "enum", "record")]
    public void ExcludingAnUnusedTagFamilyAlsoSuppressesItsImplicitCoNamedTypedef(NativeDeclarationCategory family, string excluded, string retained)
    {
        DeclarationSelection selected = Select(Source(), new() { excludeCategories = [family] });
        Assert.DoesNotContain(selected.module.declarations, declaration => declaration.id.value == excluded || declaration.id.value == "alias-" + excluded);
        Assert.Contains(selected.publicDeclarations, id => id.value == retained);
        Assert.Empty(new NativeModuleVerifier().Validate(selected.module));
    }

    [Theory]
    [InlineData(NativeDeclarationCategory.Records, "alias-record")]
    [InlineData(NativeDeclarationCategory.Records, "SharedRecord")]
    [InlineData(NativeDeclarationCategory.Enumerations, "alias-enum")]
    [InlineData(NativeDeclarationCategory.Enumerations, "SharedEnum")]
    public void ExplicitAliasStillRequiresItsExcludedTagStorage(NativeDeclarationCategory family, string alias)
    {
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => Select(Source(), new()
        { excludeCategories = [family], categories = [new(NativeDeclarationCategory.TypeAliases) { includeDeclarations = [alias] }] }));
        Assert.Contains("dependency chain", failure.Message);
        Assert.Contains("excluded category", failure.Message);
    }

    [Fact]
    public void GlobalExactAliasIdStillRequiresItsExcludedTagStorage()
    {
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => Select(Source(), new()
        { includeDeclarations = ["alias-record"], excludeCategories = [NativeDeclarationCategory.Records] }));
        Assert.Contains("excluded category", failure.Message);
        Assert.Contains("dependency chain", failure.Message);
    }

    [Fact]
    public void DistinctAliasNamesRemainIndependentRootsWithStrictStorageDependencies()
    {
        NativeModule source = Source();
        NativeDeclaration alias = source.GetDeclaration(new("alias-record")) with { name = "OtherName", qualifiedName = "OtherName" };
        NativeModule module = new(source.targetId, source.triple, source.pointerSize, source.types,
            source.declarations.Replace(source.GetDeclaration(alias.id), alias), []) { language = source.language, byteOrder = source.byteOrder };
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => Select(module, new()
        { excludeCategories = [NativeDeclarationCategory.Records] }));
        Assert.Contains("OtherName", failure.Message);
        Assert.Contains("dependency chain", failure.Message);
    }

    [Fact]
    public void CallableUseOfACoNamedAliasStillRequiresItsExcludedTagStorage()
    {
        NativeModule source = Source();
        NativeDeclaration callable = new(new("read-alias"), "Function", "read_alias", "read_alias", null, new(s_header, 10, 1))
        { role = "Function", returnType = source.GetDeclaration(new("alias-record")).type, callingConvention = "C" };
        NativeModule module = new(source.targetId, source.triple, source.pointerSize, source.types, source.declarations.Add(callable), [])
        { language = source.language, byteOrder = source.byteOrder };
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => Select(module, new()
        { excludeCategories = [NativeDeclarationCategory.Records] }));
        Assert.Contains("read_alias", failure.Message);
        Assert.Contains("dependency chain", failure.Message);
    }

    [Fact]
    public void PublicHeaderCoNamedTypedefStillPromotesItsDependencyHeaderDefinition()
    {
        NativeModule source = Source();
        SourceLocation dependency = new(Path.Combine(s_root, "dependency.h"), 1, 1);
        NativeModule module = new(source.targetId, source.triple, source.pointerSize, source.types,
            source.declarations.Select(declaration => declaration.id.value is "record" or "field" ? declaration with { source = dependency } : declaration).ToImmutableArray(), [])
        { language = source.language, byteOrder = source.byteOrder };
        DeclarationSelection selected = Select(module, new());
        Assert.Contains(new StableId("alias-record"), selected.publicDeclarations);
        Assert.DoesNotContain(new StableId("record"), selected.publicDeclarations);
        Assert.Contains(selected.module.declarations, declaration => declaration.id.value == "record");
        Assert.Empty(new NativeModuleVerifier().Validate(selected.module));
    }

    private static DeclarationSelection Select(NativeModule module, DeclarationSelectionRequest request)
        => new DeclarationSelector().Select(module, request, [s_header], s_root, includeTransitivelyReferencedHeaders: false);

    private static NativeModule WithOtherTag(NativeDeclarationCategory family)
    {
        NativeModule source = Source();
        bool record = family == NativeDeclarationCategory.Records;
        string name = record ? "OtherRecord" : "OtherEnum";
        NativeType type = new(new("other-type"), record ? NativeTypeCategory.Record : NativeTypeCategory.Enumeration, name, 4, 4)
        { declaration = new("other"), elementType = record ? null : new StableId("int") };
        NativeType alias = new(new("other-alias-type"), NativeTypeCategory.Alias, name, 4, 4) { elementType = type.id, declaration = new("other-alias") };
        NativeDeclaration tag = new(new("other"), record ? "Class" : "Enum", name, name, type.id, new(s_header, 20, 1))
        { role = record ? "Struct" : null, isCompleteDefinition = true };
        NativeDeclaration aliasDeclaration = new(new("other-alias"), "Typedef", name, name, alias.id, tag.source);
        return new(source.targetId, source.triple, source.pointerSize, source.types.AddRange(ImmutableArray.Create(type, alias)),
            source.declarations.AddRange(ImmutableArray.Create(tag, aliasDeclaration)), [])
        { language = source.language, byteOrder = source.byteOrder };
    }

    private static NativeModule Source()
    {
        NativeType integer = new(new("int"), NativeTypeCategory.Primitive, "int", 4, 4) { primitiveKind = "Int", isSigned = true };
        NativeType record = new(new("record-type"), NativeTypeCategory.Record, "struct SharedRecord", 4, 4) { declaration = new("record") };
        NativeType recordAlias = new(new("record-alias-type"), NativeTypeCategory.Alias, "SharedRecord", 4, 4)
        { elementType = record.id, declaration = new("alias-record") };
        NativeType enumeration = new(new("enum-type"), NativeTypeCategory.Enumeration, "enum SharedEnum", 4, 4)
        { elementType = integer.id, declaration = new("enum") };
        NativeType enumAlias = new(new("enum-alias-type"), NativeTypeCategory.Alias, "SharedEnum", 4, 4)
        { elementType = enumeration.id, declaration = new("alias-enum") };
        SourceLocation location = new(s_header, 1, 1);
        ImmutableArray<NativeDeclaration> declarations =
        [
            new(new("record"), "Class", "SharedRecord", "SharedRecord", record.id, location)
                { role = "Struct", isCompleteDefinition = true, children = [new("field")] },
            new(new("field"), "Field", "value", "SharedRecord::value", integer.id, location) { parent = new("record"), offset = 0 },
            new(new("alias-record"), "Typedef", "SharedRecord", "SharedRecord", recordAlias.id, location),
            new(new("enum"), "Enum", "SharedEnum", "SharedEnum", enumeration.id, location) { children = [new("enum-item")] },
            new(new("enum-item"), "EnumItem", "Entry", "SharedEnum::Entry", integer.id, location) { parent = new("enum"), value = "1" },
            new(new("alias-enum"), "Typedef", "SharedEnum", "SharedEnum", enumAlias.id, location)
        ];
        return new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8, [integer, record, recordAlias, enumeration, enumAlias], declarations, [])
        { language = SourceLanguage.C, byteOrder = NativeByteOrder.LittleEndian };
    }
}
