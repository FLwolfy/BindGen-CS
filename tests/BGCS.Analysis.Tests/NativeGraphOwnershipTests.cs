using System;
using System.Collections.Immutable;
using BGCS.Analysis.Interop;
using BGCS.Core.Common;
using BGCS.Core.Native;
using Xunit;

namespace BGCS.Analysis.Tests;

public sealed class NativeGraphOwnershipTests
{
    [Fact]
    public void SharedTypesAndBorrowedTemplateArgumentsDoNotBecomeOwnedDeclarations()
    {
        NativeType shared = new(new("shared"), NativeTypeCategory.Template, "T", null, null);
        NativeDeclaration first = Declaration("first") with { type = shared.id, templateParameters = [shared.id] };
        NativeDeclaration second = Declaration("second") with { type = shared.id, templateArguments = [new("Type", "T", shared.id)] };
        NativeModule module = new("synthetic", "synthetic-explicit", 8, [shared], [first, second], []);
        Assert.Empty(new NativeModuleVerifier().Validate(module));
        Assert.Null(first.parent);
        Assert.Null(second.parent);
        Assert.Same(shared, module.GetType(first.type!.Value));
        Assert.Same(shared, module.GetType(second.type!.Value));
    }

    [Fact]
    public void ReciprocalOwnershipIsAcceptedWithoutReparentingFrozenFacts()
    {
        NativeDeclaration child = Declaration("child") with { parent = new("owner") };
        NativeDeclaration owner = Declaration("owner") with { children = [child.id] };
        NativeModule module = Module(owner, child);
        Assert.Empty(new NativeModuleVerifier().Validate(module));
        NativeDeclaration replacement = child with { name = "replacement" };
        Assert.Equal("child", module.GetDeclaration(child.id).name);
        Assert.Equal("replacement", replacement.name);
    }

    [Theory]
    [InlineData("duplicate-child")]
    [InlineData("two-owners")]
    [InlineData("nonreciprocal-child")]
    [InlineData("missing-owned-child")]
    [InlineData("self-cycle")]
    [InlineData("ancestor-cycle")]
    public void InvalidOwnedEdgesAreRejectedThroughThePublicGraphContract(string shape)
    {
        NativeDeclaration owner = Declaration("owner"), other = Declaration("other"), child = Declaration("child");
        NativeModule module = shape switch
        {
            "duplicate-child" => Module(owner with { children = [child.id, child.id] }, child with { parent = owner.id }),
            "two-owners" => Module(owner with { children = [child.id] }, other with { children = [child.id] }, child with { parent = owner.id }),
            "nonreciprocal-child" => Module(owner with { children = [child.id] }, child),
            "missing-owned-child" => Module(owner, child with { parent = owner.id }),
            "self-cycle" => Module(owner with { parent = owner.id, children = [owner.id] }),
            "ancestor-cycle" => Module(owner with { parent = child.id, children = [child.id] }, child with { parent = owner.id, children = [owner.id] }),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        Assert.Contains(new NativeModuleVerifier().Validate(module), diagnostic => diagnostic.isFailure && diagnostic.code == "BGCS-NATIVE-GRAPH");
    }

    [Fact]
    public void SeparateOwnersDoNotReportASingleChildAsRepeatedWithinOneOwner()
    {
        NativeDeclaration first = Declaration("first"), second = Declaration("second");
        NativeDeclaration child = Declaration("child") with { parent = first.id };
        NativeModule module = Module(first with { children = [child.id] }, second with { children = [child.id] }, child);

        Assert.Collection(new NativeModuleVerifier().Validate(module),
            diagnostic => Assert.Contains("is owned by more than one declaration", diagnostic.message),
            diagnostic => Assert.Contains("does not reciprocate its declared owner", diagnostic.message));
    }

    [Fact]
    public void DeclarationIdentityCollisionsFailBeforeLookupCanSelectAnArbitraryOwner()
    {
        NativeDeclaration first = Declaration("duplicate"), second = first with { name = "different" };
        Assert.Throws<ArgumentException>(() => Module(first, second));
    }

    private static NativeDeclaration Declaration(string name) => new(new(name), "Class", name, name, null, new("fixture.hpp", 1, 1));
    private static NativeModule Module(params NativeDeclaration[] declarations)
        => new("synthetic", "synthetic-explicit", 8, [], declarations.ToImmutableArray(), []);
}
