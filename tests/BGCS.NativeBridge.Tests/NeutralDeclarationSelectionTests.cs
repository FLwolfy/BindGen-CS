using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using BGCS.Analysis.Declarations;
using BGCS.Analysis.Interop;
using BGCS.Analysis.Types;
using BGCS.Core.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.Core.Targets;
using BGCS.NativeBridge.Planning;
using Xunit;

namespace BGCS.NativeBridge.Tests;

public sealed class NeutralDeclarationSelectionTests
{
    private static readonly string s_root = Path.Combine(Path.GetTempPath(), "bgcs-neutral-selection");
    private static readonly string s_entry = Path.Combine(s_root, "entry.hpp");
    private static readonly string s_dependency = Path.Combine(s_root, "dependency.hpp");

    [Fact]
    public void EntrySelection_PreservesRequiredStorageWithoutPromotingDependencyFunctions()
    {
        DeclarationSelection selected = Select(new(), transitive: false);
        Assert.Contains(new StableId("read"), selected.callables);
        Assert.DoesNotContain(new StableId("release"), selected.callables);
        Assert.Contains(selected.module.declarations, declaration => declaration.id == new StableId("item"));
        Assert.Contains(selected.module.declarations, declaration => declaration.id == new StableId("item-field"));
        Assert.DoesNotContain(selected.module.declarations, declaration => declaration.id == new StableId("release"));
        Assert.Equal(NativeByteOrder.LittleEndian, selected.module.byteOrder);
        Assert.Empty(new NativeModuleVerifier().Validate(selected.module));
    }

    [Fact]
    public void ExactHeaderIncludes_CanSelectAnAlreadyParsedDependencyHeader()
    {
        DeclarationSelection selected = Select(new() { includeHeaders = ["dependency.hpp"] }, transitive: false);
        Assert.Equal(new[] { new StableId("release") }, selected.callables);
        Assert.DoesNotContain(new StableId("read"), selected.publicDeclarations);
        Assert.Contains(new StableId("item"), selected.publicDeclarations);
    }

    [Fact]
    public void ExcludingNamespaceOriginalHeader_DoesNotExcludeItsDeclarationsInOtherHeaders()
    {
        DeclarationSelection selected = Select(new() { includeDeclarations = ["Demo::Plain"], excludeHeaders = ["dependency.hpp"] });
        Assert.Equal(new[] { new StableId("plain") }, selected.callables);
        Assert.Contains(selected.module.declarations, declaration => declaration.kind == "Namespace");
        Assert.DoesNotContain(selected.module.declarations, declaration => declaration.id == new StableId("item"));
    }

    [Theory]
    [InlineData("Demo::Item")]
    [InlineData("Demo::Item::value")]
    public void ExcludingRequiredRecordOrField_ReportsACompleteDependencyConflict(string excluded)
    {
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => Select(new()
            { includeDeclarations = ["Demo::ReadItem"], excludeDeclarations = [excluded] }));
        Assert.Contains("required native storage or cleanup", failure.Message);
        Assert.Contains("ReadItem", failure.Message);
        Assert.Contains(excluded, failure.Message);
        Assert.Contains("dependency chain", failure.Message);
        Assert.Contains("shim contract", failure.Message);
    }

    [Fact]
    public void ExcludingRequiredHeader_DoesNotTruncateThePointerPointee()
    {
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => Select(new()
            { includeDeclarations = ["Demo::ReadItem"], excludeHeaders = ["dependency.hpp"] }));
        Assert.Contains(s_dependency, failure.Message);
        Assert.Contains("Demo::Item", failure.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExcludingExplicitCleanupByHeaderOrDeclaration_IsRejected(bool byHeader)
    {
        DeclarationSelectionRequest request = new() { includeDeclarations = ["Demo::Plain"],
            excludeHeaders = byHeader ? ["dependency.hpp"] : [], excludeDeclarations = byHeader ? [] : ["Demo::Release"] };
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => Select(request, required: ["Demo::Release"]));
        Assert.Contains("Release", failure.Message);
        Assert.Contains("required native storage or cleanup", failure.Message);
    }

    [Fact]
    public void ExplicitRequiredCleanup_RemainsCallableWithoutPromotingItsNeighbors()
    {
        DeclarationSelection selected = Select(new() { includeDeclarations = ["Demo::Plain"] }, required: ["Demo::Release", "Demo::Release"]);
        Assert.Equal(new[] { "plain", "release" }, selected.callables.Select(id => id.value));
        Assert.Equal(new[] { new StableId("plain") }, selected.publicDeclarations);
        Assert.DoesNotContain(new StableId("read"), selected.callables);
    }

    [Fact]
    public void QualifiedOverloadWithoutSignature_IsRejectedAsAmbiguous()
    {
        ArgumentException failure = Assert.Throws<ArgumentException>(() => Select(new() { includeDeclarations = ["Demo::Overloaded"] }));
        Assert.Contains("ambiguous", failure.Message);
        Assert.Contains("sourceSignature", failure.Message);
    }

    [Fact]
    public void ExactSignatureAndStableIdentity_SelectOnlyOneOverload()
    {
        NativeModule source = Source();
        string signature = new NativeTypeFormatter().CallableSignature(source, source.GetDeclaration(new("overload-int")));
        Assert.Equal(new[] { new StableId("overload-int") }, Select(new() { includeDeclarations = [signature] }).callables);
        Assert.Equal(new[] { new StableId("overload-float") }, Select(new() { includeDeclarations = ["overload-float"] }).callables);
    }

    [Fact]
    public void ClassSelectionIncludesAccessibleMembers_AndExcludedConstructorCannotBecomeSyntheticDefault()
    {
        DeclarationSelection selected = Select(new() { includeDeclarations = ["Demo::Service"], excludeDeclarations = ["Demo::Service::Service(int)"] });
        Assert.Equal(new[] { new StableId("service-read") }, selected.callables);
        Assert.Contains(selected.module.declarations, declaration => declaration.id == new StableId("service-constructor"));
        Assert.DoesNotContain(new StableId("service-constructor"), selected.publicDeclarations);
        NativeBridgePlan plan = Plan(selected);
        Assert.DoesNotContain(plan.exports, export => export.operation.kind is BridgeOperationKind.Construct or BridgeOperationKind.DefaultConstruct);
        Assert.Equal(BridgeOperationKind.InstanceCall, Assert.Single(plan.exports).operation.kind);
    }

    [Fact]
    public void PrivateBehaviorMetadata_IsRetainedWithoutPublishingItsUnrelatedReturnType()
    {
        DeclarationSelection selected = Select(new() { includeDeclarations = ["Demo::Behavior"] });
        Assert.Empty(selected.callables);
        Assert.Contains(selected.module.declarations, declaration => declaration.id == new StableId("behavior-hidden"));
        Assert.Contains(selected.module.declarations, declaration => declaration.id == new StableId("unrelated"));
        NativeBridgePlan plan = Plan(selected);
        Assert.DoesNotContain(plan.types, type => type.nativeType == new StableId("unrelated-type"));
        Assert.DoesNotContain(plan.types, type => type.kind == BridgeTypeKind.Record);
        Assert.Contains(plan.exports, export => export.operation.kind == BridgeOperationKind.DefaultConstruct);
    }

    [Fact]
    public void SelectingOnlyAFunction_DoesNotInventConstructionForDependencyOnlyClasses()
    {
        DeclarationSelection selected = Select(new() { includeDeclarations = ["Demo::ReadItem"] });
        NativeBridgePlan plan = Plan(selected);
        Assert.DoesNotContain(plan.exports, export => export.operation.kind is BridgeOperationKind.Construct or BridgeOperationKind.DefaultConstruct);
    }

    [Fact]
    public void UnknownExactSelector_FailsWithInspectGuidance()
    {
        ArgumentException failure = Assert.Throws<ArgumentException>(() => Select(new() { includeDeclarations = ["Demo::Missing"] }));
        Assert.Contains("matches no native declaration", failure.Message);
        Assert.Contains("inspect", failure.Message);
    }

    [Fact]
    public void ExplicitGeneratedRoot_PreservesOrdinarySelectionWithoutPromotingOtherReparsedDeclarations()
    {
        DeclarationSelection selected = new DeclarationSelector(global::System.StringComparer.Ordinal).Select(Source(), new() { includeDeclarations = ["Demo::Plain"] }, [s_entry], s_root,
            additionalPublicDeclarations: ["release"], ordinaryDeclarations: [new("plain")]);
        Assert.Equal(new[] { "plain", "release" }, selected.callables.Select(id => id.value));
        Assert.DoesNotContain(new StableId("read"), selected.publicDeclarations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitGeneratedRoot_CannotRestoreAnExcludedDeclarationOrHeader(bool byHeader)
    {
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => new DeclarationSelector(global::System.StringComparer.Ordinal).Select(Source(), new()
        {
            includeDeclarations = ["Demo::Plain"], excludeHeaders = byHeader ? ["dependency.hpp"] : [],
            excludeDeclarations = byHeader ? [] : ["Demo::Release"]
        }, [s_entry], s_root, additionalPublicDeclarations: ["release"]));
        Assert.Contains("explicit generation request", failure.Message);
        Assert.Contains("Release", failure.Message);
    }

    [Fact]
    public void ExplicitClassRoot_HonorsAnExcludedConstructorWithoutInventingAnother()
    {
        DeclarationSelection selected = new DeclarationSelector(global::System.StringComparer.Ordinal).Select(Source(), new()
        { includeDeclarations = ["Demo::Plain"], excludeDeclarations = ["Demo::Service::Service(int)"] }, [s_entry], s_root,
            additionalPublicDeclarations: ["service"], ordinaryDeclarations: [new("plain")]);
        Assert.Equal(new[] { "plain", "service-read" }, selected.callables.Select(id => id.value));
        Assert.DoesNotContain(Plan(selected).exports, export => export.operation.kind is BridgeOperationKind.Construct or BridgeOperationKind.DefaultConstruct);
    }

    [Fact]
    public void RequiredAdapterMetadata_IsCheckedWithoutBecomingACallableOrPublicClassRoot()
    {
        DeclarationSelection selected = new DeclarationSelector(global::System.StringComparer.Ordinal).Select(Source(), new() { includeDeclarations = ["Demo::Plain"] }, [s_entry], s_root,
            requiredMetadataDeclarations: ["service-read"]);
        Assert.Equal(new[] { new StableId("plain") }, selected.callables);
        Assert.Equal(new[] { new StableId("plain") }, selected.publicDeclarations);
        Assert.Contains(selected.module.declarations, declaration => declaration.id == new StableId("service-read"));
        Assert.DoesNotContain(Plan(selected).exports, export => export.operation.kind == BridgeOperationKind.DefaultConstruct);
        Assert.Throws<InvalidOperationException>(() => new DeclarationSelector(global::System.StringComparer.Ordinal).Select(Source(), new()
        { includeDeclarations = ["Demo::Plain"], excludeDeclarations = ["Demo::Service::Read"] }, [s_entry], s_root,
            requiredMetadataDeclarations: ["service-read"]));
    }

    [Fact]
    public void CallableCategoryKeepsRecordStorageWithoutPublishingRecordRoots()
    {
        DeclarationSelection selected = Select(new() { includeCategories = [NativeDeclarationCategory.Callables] });
        Assert.Contains(new StableId("read"), selected.callables);
        Assert.DoesNotContain(new StableId("item"), selected.publicDeclarations);
        Assert.Contains(selected.module.declarations, declaration => declaration.id == new StableId("item-field"));
        Assert.Empty(new NativeModuleVerifier().Validate(selected.module));
    }

    [Fact]
    public void RecordCategoryDoesNotPromoteMetadataMembersToCallableRoots()
    {
        DeclarationSelection selected = Select(new() { includeCategories = [NativeDeclarationCategory.Records] });
        Assert.Empty(selected.callables);
        Assert.Contains(new StableId("service"), selected.publicDeclarations);
        Assert.DoesNotContain(new StableId("service-read"), selected.publicDeclarations);
        Assert.Contains(selected.module.declarations, declaration => declaration.id == new StableId("service-read"));
    }

    [Fact]
    public void ExcludedRecordCategoryCannotTruncateRequiredCallableStorage()
    {
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => Select(new()
        { includeDeclarations = ["Demo::ReadItem"], excludeCategories = [NativeDeclarationCategory.Records] }));
        Assert.Contains("excluded category 'Records'", failure.Message);
        Assert.Contains("ReadItem", failure.Message);
        Assert.Contains("dependency chain", failure.Message);
    }

    [Fact]
    public void ExplicitAdapterBypassesOnlyPositiveCategoryFilters()
    {
        DeclarationSelection selected = new DeclarationSelector(global::System.StringComparer.Ordinal).Select(Source(), new()
        { includeCategories = [NativeDeclarationCategory.Enumerations] }, [s_entry], s_root, additionalPublicDeclarations: ["release"]);
        Assert.Equal(new[] { new StableId("release") }, selected.callables);
        Assert.Throws<InvalidOperationException>(() => new DeclarationSelector(global::System.StringComparer.Ordinal).Select(Source(), new()
        { excludeCategories = [NativeDeclarationCategory.Callables] }, [s_entry], s_root, additionalPublicDeclarations: ["release"]));
    }

    [Theory]
    [InlineData(NativeDeclarationCategory.Callbacks, "callback")]
    [InlineData(NativeDeclarationCategory.TypeAliases, "integer-alias")]
    public void CallbackPointerAliasesHaveASeparateTypedCategory(NativeDeclarationCategory category, string expected)
    {
        NativeType integer = new(new("int"), NativeTypeCategory.Primitive, "int", 4, 4) { primitiveKind = "Int", isSigned = true };
        NativeType function = new(new("function-type"), NativeTypeCategory.Function, "int(int)", null, null)
        { returnType = integer.id, parameterTypes = [integer.id], callingConvention = "C", hasPrototype = true };
        NativeType pointer = new(new("pointer"), NativeTypeCategory.Pointer, "int(*)(int)", 8, 8) { elementType = function.id };
        NativeType callback = new(new("callback-type"), NativeTypeCategory.Alias, "Callback", 8, 8) { elementType = pointer.id, declaration = new("callback") };
        NativeType alias = new(new("integer-alias-type"), NativeTypeCategory.Alias, "Integer", 4, 4) { elementType = integer.id, declaration = new("integer-alias") };
        SourceLocation location = new(s_entry, 1, 1);
        NativeModule source = new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8, [integer, function, pointer, callback, alias],
            [new(new("callback"), "Typedef", "Callback", "Callback", callback.id, location),
             new(new("integer-alias"), "Typedef", "Integer", "Integer", alias.id, location)], []);
        DeclarationSelection selected = new DeclarationSelector(global::System.StringComparer.Ordinal).Select(source, new() { includeCategories = [category] }, [s_entry], s_root);
        Assert.Equal(new StableId(expected), Assert.Single(selected.publicDeclarations));
        Assert.Empty(new NativeModuleVerifier().Validate(selected.module));
    }

    private static DeclarationSelection Select(DeclarationSelectionRequest request, bool transitive = true, string[]? required = null)
        => new DeclarationSelector(global::System.StringComparer.Ordinal).Select(Source(), request, [s_entry], s_root, transitive, requiredDeclarations: required);

    [Fact]
    public void IndependentFamilyAllowlistsDoNotSuppressOtherFamiliesOrRequiredStorage()
    {
        DeclarationSelection selected = Select(new()
        {
            categories = [new(NativeDeclarationCategory.Callables) { includeDeclarations = ["Demo::Plain"] },
                new(NativeDeclarationCategory.Records) { includeDeclarations = ["Demo::Item"] }]
        });
        Assert.Equal(new[] { "plain" }, selected.callables.Select(id => id.value));
        Assert.Equal(new[] { "item", "plain" }, selected.publicDeclarations.Select(id => id.value));
        Assert.Contains(selected.module.declarations, declaration => declaration.id == new StableId("item-field"));
        DeclarationSelection dependency = Select(new()
        {
            categories = [new(NativeDeclarationCategory.Callables) { includeDeclarations = ["Demo::ReadItem"] },
                new(NativeDeclarationCategory.Records) { includeDeclarations = ["Demo::Service"] }]
        });
        Assert.DoesNotContain(new StableId("item"), dependency.publicDeclarations);
        Assert.Contains(dependency.module.declarations, declaration => declaration.id == new StableId("item"));
        Assert.Contains(dependency.module.declarations, declaration => declaration.id == new StableId("item-field"));
        Assert.Empty(new NativeModuleVerifier().Validate(dependency.module));
    }

    [Fact]
    public void FamilyExclusionsStillProtectRequiredNativeStorageAndExplicitAdapters()
    {
        DeclarationSelectionRequest request = new()
        {
            categories = [new(NativeDeclarationCategory.Callables) { includeDeclarations = ["Demo::ReadItem"] },
                new(NativeDeclarationCategory.Records) { excludeDeclarations = ["Demo::Item"] }]
        };
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => Select(request));
        Assert.Contains("excluded family declaration", failure.Message);
        Assert.Contains("dependency chain", failure.Message);
        Assert.Contains("ReadItem", failure.Message);
        Assert.Throws<InvalidOperationException>(() => new DeclarationSelector(global::System.StringComparer.Ordinal).Select(Source(), new()
        { categories = [new(NativeDeclarationCategory.Callables) { excludeDeclarations = ["Demo::Release"] }] },
            [s_entry], s_root, additionalPublicDeclarations: ["release"]));
    }

    [Fact]
    public void RequiredCleanupBypassesPositiveFamilyFiltersWithoutBecomingAnOrdinaryRoot()
    {
        DeclarationSelection selected = Select(new()
        { categories = [new(NativeDeclarationCategory.Callables) { includeDeclarations = ["Demo::Plain"] }] }, required: ["Demo::Release"]);
        Assert.Contains(new StableId("plain"), selected.callables);
        Assert.Contains(new StableId("release"), selected.callables);
        Assert.DoesNotContain(new StableId("release"), selected.publicDeclarations);
    }

    [Theory]
    [InlineData("wrong-family")]
    [InlineData("ambiguous")]
    [InlineData("same-identity")]
    [InlineData("global-conflict")]
    public void FamilySelectorsRequireExactUnambiguousConsistentIdentities(string invalid)
    {
        DeclarationSelectionRequest request = invalid switch
        {
            "wrong-family" => new() { categories = [new(NativeDeclarationCategory.Records) { includeDeclarations = ["Demo::Plain"] }] },
            "ambiguous" => new() { categories = [new(NativeDeclarationCategory.Callables) { includeDeclarations = ["Demo::Overloaded"] }] },
            "same-identity" => new() { categories = [new(NativeDeclarationCategory.Callables) { includeDeclarations = ["plain"], excludeDeclarations = ["Demo::Plain"] }] },
            _ => new() { includeDeclarations = ["Demo::Plain"], categories = [new(NativeDeclarationCategory.Callables) { excludeDeclarations = ["plain"] }] }
        };
        Assert.Throws<ArgumentException>(() => Select(request));
    }

    private static NativeBridgePlan Plan(DeclarationSelection selection)
    {
        NativeTargetDescriptor target = new(new(selection.module.targetId), "windows", "x64", "msvc", selection.module.triple, new());
        return new NativeBridgePlanner().Plan(new(selection.module, target, "selection", [s_entry]), selection.callables, publicDeclarations: selection.publicDeclarations);
    }

    private static NativeModule Source()
    {
        SourceLocation entry = new(s_entry, 1, 1), dependency = new(s_dependency, 1, 1);
        NativeType integer = new(new("int"), NativeTypeCategory.Primitive, "int", 4, 4) { primitiveKind = "Int", isSigned = true };
        NativeType floating = new(new("float"), NativeTypeCategory.Primitive, "float", 4, 4) { primitiveKind = "Float" };
        NativeType empty = new(new("void"), NativeTypeCategory.Primitive, "void", 0, null) { primitiveKind = "Void" };
        NativeType item = new(new("item-type"), NativeTypeCategory.Record, "Demo::Item", 4, 4) { declaration = new("item") };
        NativeType itemPointer = new(new("item-pointer"), NativeTypeCategory.Pointer, "Demo::Item*", 8, 8) { elementType = item.id };
        NativeType service = new(new("service-type"), NativeTypeCategory.Record, "Demo::Service", 4, 4) { declaration = new("service") };
        NativeType behavior = new(new("behavior-type"), NativeTypeCategory.Record, "Demo::Behavior", 4, 4) { declaration = new("behavior") };
        NativeType unrelated = new(new("unrelated-type"), NativeTypeCategory.Record, "Demo::Unrelated", 4, 4) { declaration = new("unrelated") };
        NativeDeclaration scope = new(new("namespace"), "Namespace", "Demo", "Demo", null, dependency)
        { children = [new("item"), new("read"), new("plain"), new("release"), new("overload-int"), new("overload-float"), new("service"), new("behavior"), new("unrelated")] };
        NativeDeclaration itemDeclaration = Record("item", "Item", item.id, dependency, [new("item-field")]);
        NativeDeclaration itemField = new(new("item-field"), "Field", "value", "Demo::Item::value", integer.id, dependency) { parent = itemDeclaration.id, offset = 0 };
        NativeDeclaration read = Function("read", "ReadItem", entry, integer.id, [new("read-input")]);
        NativeDeclaration readInput = Parameter("read-input", "input", read.id, itemPointer.id, entry);
        NativeDeclaration plain = Function("plain", "Plain", entry, integer.id, []);
        NativeDeclaration release = Function("release", "Release", dependency, empty.id, [new("release-input")]);
        NativeDeclaration releaseInput = Parameter("release-input", "input", release.id, itemPointer.id, dependency);
        NativeDeclaration intOverload = Function("overload-int", "Overloaded", entry, integer.id, [new("overload-int-input")]);
        NativeDeclaration floatOverload = Function("overload-float", "Overloaded", entry, integer.id, [new("overload-float-input")]);
        NativeDeclaration serviceDeclaration = Record("service", "Service", service.id, entry, [new("service-field"), new("service-constructor"), new("service-read")]) with { role = "Class" };
        NativeDeclaration serviceField = new(new("service-field"), "Field", "value", "Demo::Service::value", integer.id, entry) { parent = serviceDeclaration.id, offset = 0 };
        NativeDeclaration constructor = Function("service-constructor", "Service", entry, empty.id, [new("constructor-input")])
            with { parent = serviceDeclaration.id, qualifiedName = "Demo::Service::Service", role = "Constructor" };
        NativeDeclaration serviceRead = Function("service-read", "Read", entry, integer.id, []) with { parent = serviceDeclaration.id, qualifiedName = "Demo::Service::Read", isConst = true };
        NativeDeclaration behaviorDeclaration = Record("behavior", "Behavior", behavior.id, entry, [new("behavior-field"), new("behavior-hidden")]);
        NativeDeclaration behaviorField = new(new("behavior-field"), "Field", "value", "Demo::Behavior::value", integer.id, entry) { parent = behaviorDeclaration.id, offset = 0 };
        NativeDeclaration hidden = Function("behavior-hidden", "Hidden", entry, unrelated.id, []) with { parent = behaviorDeclaration.id, qualifiedName = "Demo::Behavior::Hidden", visibility = "Private" };
        NativeDeclaration unrelatedDeclaration = Record("unrelated", "Unrelated", unrelated.id, dependency, [new("unrelated-field")]);
        NativeDeclaration unrelatedField = new(new("unrelated-field"), "Field", "value", "Demo::Unrelated::value", integer.id, dependency) { parent = unrelatedDeclaration.id, offset = 0 };
        return new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8, [integer, floating, empty, item, itemPointer, service, behavior, unrelated],
            [scope, itemDeclaration, itemField, read, readInput, plain, release, releaseInput, intOverload, floatOverload,
                Parameter("overload-int-input", "input", intOverload.id, integer.id, entry), Parameter("overload-float-input", "input", floatOverload.id, floating.id, entry),
                serviceDeclaration, serviceField, constructor, Parameter("constructor-input", "input", constructor.id, integer.id, entry), serviceRead,
                behaviorDeclaration, behaviorField, hidden, unrelatedDeclaration, unrelatedField], [])
        { language = SourceLanguage.Cpp, byteOrder = NativeByteOrder.LittleEndian };

        NativeDeclaration Record(string id, string name, StableId type, SourceLocation location, ImmutableArray<StableId> children)
            => new(new(id), "Class", name, "Demo::" + name, type, location) { parent = scope.id, role = "Struct", children = children,
                isCompleteDefinition = true, isDefaultConstructible = true, isStandardLayout = true, isTriviallyCopyable = true };
        NativeDeclaration Function(string id, string name, SourceLocation location, StableId result, ImmutableArray<StableId> parameters)
            => new(new(id), "Function", name, "Demo::" + name, null, location) { parent = scope.id, returnType = result, children = parameters, role = "Function", callingConvention = "C" };
        static NativeDeclaration Parameter(string id, string name, StableId parent, StableId type, SourceLocation location)
            => new(new(id), "Parameter", name, name, type, location) { parent = parent };
    }
}
