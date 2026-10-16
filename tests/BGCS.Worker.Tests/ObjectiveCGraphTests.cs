using System;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Analysis.Declarations;
using BGCS.Analysis.Interop;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using Xunit;

namespace BGCS.Worker.Tests;

public sealed class ObjectiveCGraphTests
{
    [Fact]
    public void NeutralClosureKeepsObjectiveCProtocolsGenericParametersAndPropertyAccessors()
    {
        NativeModule source = Source();
        Assert.Empty(new NativeModuleVerifier().Validate(source));
        StableId[] closure = DependencyClosureBuilder.Build(DeclarationGraph.Create(source), [new("root")]).ToArray();
        Assert.Equal(new[] { "getter", "property", "protocol", "root" }, closure.Select(id => id.value).Order(StringComparer.Ordinal));
        DeclarationSelection selection = new DeclarationSelector().Select(source, new() { includeDeclarations = ["Root"] }, ["facts.h"], ".");
        Assert.Empty(new NativeModuleVerifier().Validate(selection.module));
        Assert.Contains(selection.module.types, type => type.id.value == "generic");
        Assert.Contains(selection.module.declarations, declaration => declaration.id.value == "protocol");
        Assert.Contains(selection.module.declarations, declaration => declaration.id.value == "property");
        Assert.Contains(selection.module.declarations, declaration => declaration.id.value == "getter");
    }

    [Theory]
    [InlineData("missing-protocol")]
    [InlineData("wrong-protocol-kind")]
    [InlineData("missing-generic")]
    [InlineData("missing-category-target")]
    [InlineData("method-dispatch")]
    [InlineData("accessor-parent")]
    [InlineData("accessor-type")]
    [InlineData("property-attributes")]
    [InlineData("guessed-evidence")]
    [InlineData("cpp-inheritance")]
    [InlineData("c-export")]
    public void InvalidObjectiveCReferencesAndFabricatedRelationshipsAreRejected(string mutation)
    {
        NativeModule source = Source();
        NativeDeclaration root = source.GetDeclaration(new("root")), getter = source.GetDeclaration(new("getter")), property = source.GetDeclaration(new("property"));
        NativeDeclaration changed = mutation switch
        {
            "missing-protocol" => root with { objectiveCContainer = root.objectiveCContainer! with { protocols = [new("missing")] } },
            "wrong-protocol-kind" => root with { objectiveCContainer = root.objectiveCContainer! with { protocols = [root.id] } },
            "missing-generic" => root with { objectiveCContainer = root.objectiveCContainer! with { genericParameters = [new("missing")] } },
            "missing-category-target" => root with { objectiveCContainer = new(NativeObjectiveCContainerKind.Category, Proof()) { categoryName = "Extras" } },
            "method-dispatch" => getter with { objectiveCMethod = getter.objectiveCMethod! with { dispatch = NativeObjectiveCDispatchKind.Unknown } },
            "accessor-parent" => getter with { parent = new StableId("protocol") },
            "accessor-type" => getter with { returnType = new StableId("generic") },
            "property-attributes" => property with { objectiveCProperty = property.objectiveCProperty! with { attributes = (NativeObjectiveCPropertyAttributes)8192 } },
            "guessed-evidence" => root with { objectiveCContainer = root.objectiveCContainer! with { evidence = new(EvidenceLevel.UserAsserted, "guess") } },
            "cpp-inheritance" => root with { baseClasses = [new(new("root-type"), "Public", false)] },
            "c-export" => getter with { linkage = "C", callingConvention = "C", addressability = NativeCallableAddressability.Addressable, addressabilityEvidence = Proof() },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        NativeModule malformed = new(source.targetId, source.triple, source.pointerSize, source.types,
            source.declarations.Select(declaration => declaration.id == changed.id ? changed : declaration).ToImmutableArray(), []) { language = SourceLanguage.ObjC };
        Assert.Contains(new NativeModuleVerifier().Validate(malformed), diagnostic => diagnostic.isFailure && diagnostic.message.Contains("Objective-C", StringComparison.Ordinal));
    }

    private static NativeModule Source()
    {
        NativeType integer = new(new("integer"), NativeTypeCategory.Primitive, "int", 4, 4) { primitiveKind = "Int", isSigned = true };
        NativeType generic = new(new("generic"), NativeTypeCategory.Template, "T", null, null);
        NativeType record = new(new("root-type"), NativeTypeCategory.Record, "Root", null, null) { declaration = new StableId("root") };
        NativeDeclaration root = new(new("root"), "Class", "Root", "Root", record.id, new("facts.h", 1, 1))
        { children = [new("getter"), new("property")], objectiveCContainer = new(NativeObjectiveCContainerKind.Interface, Proof()) { protocols = [new("protocol")], genericParameters = [generic.id] } };
        NativeDeclaration protocol = new(new("protocol"), "Class", "Protocol", "Protocol", null, new("facts.h", 2, 1))
        { objectiveCContainer = new(NativeObjectiveCContainerKind.Protocol, Proof()) };
        NativeDeclaration getter = new(new("getter"), "Function", "count", "Root::count", null, new("facts.h", 3, 1))
        { parent = root.id, returnType = integer.id, linkage = "Objective-C", objectiveCMethod = new("count", NativeObjectiveCDispatchKind.Instance, Proof()) };
        NativeDeclaration property = new(new("property"), "Property", "count", "Root::count", integer.id, new("facts.h", 4, 1))
        { parent = root.id, objectiveCProperty = new("count", null, Proof()) { attributes = NativeObjectiveCPropertyAttributes.ReadOnly, getter = getter.id } };
        return new("objc-facts", "arm64-apple-darwin", 8, [integer, generic, record], [root, protocol, getter, property], []) { language = SourceLanguage.ObjC };
    }

    private static Evidence Proof() => new(EvidenceLevel.Declared, "neutral-objective-c-fixture");
}
