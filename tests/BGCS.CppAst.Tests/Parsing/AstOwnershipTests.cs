using System;
using BGCS.CppAst.Collections;
using BGCS.CppAst.Model.Declarations;
using Xunit;

namespace BGCS.CppAst.Tests;

public sealed class AstOwnershipTests
{
    [Fact]
    public void TypedLookupReturnsTheRequestedOverloadOrNull()
    {
        CppGlobalDeclarationContainer container = new(default);
        CppClass record = new(default, "shared");
        CppFunction function = new(default, "shared");
        container.classes.Add(record);
        container.functions.Add(function);
        Assert.Same(record, container.FindByName<CppClass>("shared"));
        Assert.Same(function, container.FindByName<CppFunction>("shared"));
        Assert.Null(container.FindByName<CppEnum>("shared"));
        Assert.Null(container.FindByFullName<CppEnum>("shared"));
        Assert.Null(container.FindByName("missing"));
        Assert.Throws<ArgumentNullException>(() => container.FindByName(null!, "shared"));
    }

    [Fact]
    public void ReplacementDetachesThePreviousNodeAndAttachesTheNewNode()
    {
        CppNamespace owner = new(default, "owner");
        CppClass previous = new(default, "previous");
        CppClass replacement = new(default, "replacement");
        owner.classes.Add(previous);
        owner.classes[0] = replacement;
        Assert.Null(previous.parent);
        Assert.Same(owner, replacement.parent);
        owner.classes[0] = replacement;
        Assert.Same(owner, replacement.parent);
    }

    [Fact]
    public void InvalidInsertAndReplacementPreserveAllOwners()
    {
        CppNamespace owner = new(default, "owner");
        CppNamespace other = new(default, "other");
        CppClass retained = new(default, "retained");
        CppClass borrowed = new(default, "borrowed");
        CppClass detached = new(default, "detached");
        owner.classes.Add(retained);
        other.classes.Add(borrowed);
        Assert.Throws<ArgumentException>(() => owner.classes[0] = borrowed);
        Assert.Throws<ArgumentOutOfRangeException>(() => owner.classes.Insert(2, detached));
        Assert.Throws<ArgumentNullException>(() => owner.classes.Add(null!));
        Assert.Same(retained, owner.classes[0]);
        Assert.Same(owner, retained.parent);
        Assert.Same(other, borrowed.parent);
        Assert.Null(detached.parent);
    }

    [Fact]
    public void ContainersRejectSelfOwnershipAndAncestorCycles()
    {
        CppClass owner = new(default, "owner");
        CppClass child = new(default, "child");
        Assert.Throws<ArgumentException>(() => owner.classes.Add(owner));
        owner.classes.Add(child);
        Assert.Throws<ArgumentException>(() => child.classes.Add(owner));
        Assert.Null(owner.parent);
        Assert.Same(owner, child.parent);
    }

    [Fact]
    public void QualifiedNamesFollowCurrentParentNamesAndReparenting()
    {
        CppNamespace first = new(default, "first");
        CppNamespace second = new(default, "second");
        CppClass item = new(default, "item");
        first.classes.Add(item);
        Assert.Equal("first", item.fullParentName);
        first.name = "renamed";
        Assert.Equal("renamed", item.fullParentName);
        Assert.True(first.classes.Remove(item));
        second.classes.Add(item);
        Assert.Equal("second", item.fullParentName);
    }
}
