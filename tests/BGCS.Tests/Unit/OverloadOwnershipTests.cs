using System.Collections.Generic;
using BGCS.CppAst.Model.Types;
using BGCS.CSharp;
using Xunit;

namespace BGCS.Tests.Unit;

public sealed class OverloadOwnershipTests
{
    [Fact]
    public void TypeClonePreservesReadOnlyReferenceClassification()
    {
        CsType source = new("in int", CsPrimitiveType.Int);
        CsType clone = source.Clone();
        Assert.True(clone.isIn);
        Assert.Equal(source.GetConflictHashCode(), clone.GetConflictHashCode());
        Assert.True(source.Conflicts(clone));
    }

    [Fact]
    public void CapturedSignatureRemainsStableAfterInputAndExposedCopiesChange()
    {
        CsParameterInfo source = new("value", CppPrimitiveType.@int, new CsType("int", CsPrimitiveType.Int), Direction.In);
        List<CsParameterInfo> parameters = [source];
        ValueVariation captured = new("Call", parameters);
        ValueVariation equivalent = new("Call", parameters);
        HashSet<ValueVariation> set = [captured];
        source.type.name = "long";
        source.defaultValue = "7";
        parameters.Clear();
        captured.parameters[0].type.name = "byte";
        Assert.Contains(equivalent, set);
        Assert.Equal("int", captured.parameters[0].type.name);
        Assert.Null(captured.parameters[0].defaultValue);
        Assert.True(default(ValueVariation).Equals(default(ValueVariation)));
        Assert.Equal(0, default(ValueVariation).GetHashCode());
    }

    [Fact]
    public void UpdatingAnAbsentVariationDoesNotAddTheReplacement()
    {
        CsType returnType = new("void", CsPrimitiveType.Void);
        CsFunctionOverload overload = new("call", "Call", null, string.Empty, CsFunctionKind.Default, returnType);
        CsFunctionVariation absent = new(string.Empty, "call", "Call", string.Empty, CsFunctionKind.Default, returnType);
        CsFunctionVariation replacement = absent.Clone();
        Assert.False(overload.TryUpdateVariation(absent, replacement));
        Assert.Empty(overload.variations);
        Assert.Empty(overload.valueVariations);
    }
}
