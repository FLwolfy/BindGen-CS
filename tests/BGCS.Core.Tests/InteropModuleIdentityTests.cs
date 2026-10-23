using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using Xunit;

namespace BGCS.Core.Tests;

public sealed class InteropModuleIdentityTests
{
    [Fact]
    public void FunctionLookupRetainsExactImmutableNodesAfterInputOrderAndReplacementChange()
    {
        InteropType integer = Integer();
        InteropFunction first = new(new("first"), "same_export", integer.id, [], "C");
        InteropFunction second = new(new("second"), "same_export", integer.id, [], "C");
        var builder = ImmutableArray.CreateBuilder<InteropFunction>();
        builder.Add(second); builder.Add(first);
        InteropModule original = new("fixture", "fixture-target", 8, [integer], builder.ToImmutable(), []);
        InteropFunction replacement = first with { exportName = "changed_export", parameters = [new(new("argument"), "argument", integer.id)] };
        builder.Clear(); builder.Add(replacement); builder.Add(second);
        InteropModule next = new("fixture", "fixture-target", 8, [integer], builder.ToImmutable(), []);
        builder.Clear();
        Assert.Same(first, original.GetFunction(first.id));
        Assert.Same(second, original.GetFunction(second.id));
        Assert.Same(replacement, next.GetFunction(first.id));
        Assert.Empty(original.GetFunction(first.id).parameters);
        Assert.Single(next.GetFunction(first.id).parameters);
        Assert.Equal(second.id, original.functions[0].id);
    }

    [Fact]
    public void MissingOrDifferentlyCasedFunctionIdentitiesCannotResolveByExportSpelling()
    {
        InteropType integer = Integer();
        InteropFunction function = new(new("identity"), "native_export", integer.id, [], "C");
        InteropModule module = new("fixture", "fixture-target", 8, [integer], [function], []);
        Assert.Throws<KeyNotFoundException>(() => module.GetFunction(new("Identity")));
        Assert.Throws<KeyNotFoundException>(() => module.GetFunction(new("native_export")));
        Assert.Throws<KeyNotFoundException>(() => module.GetFunction(new("missing")));
    }

    [Fact]
    public void DuplicateFunctionIdentitiesAreRejectedEvenWhenExportNamesAndSignaturesDiffer()
    {
        InteropType integer = Integer();
        InteropFunction original = new(new("duplicate"), "first_export", integer.id, [], "C");
        InteropFunction changed = original with { exportName = "second_export", parameters = [new(new("argument"), "argument", integer.id)] };
        Assert.Throws<ArgumentException>(() => new InteropModule("fixture", "fixture-target", 8, [integer], [original, changed], []));
    }

    private static InteropType Integer() => new(new("integer"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
}
