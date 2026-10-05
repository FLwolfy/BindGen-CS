using System;
using System.Collections.Generic;
using System.Globalization;
using BGCS.CppAst.AttributeParsing;
using BGCS.CppAst.Model.Templates;
using BGCS.CppAst.Model.Types;
using ClangSharp.Interop;
using Xunit;

namespace BGCS.CppAst.Tests;

public sealed class AttributeArgumentTests
{
    [Fact]
    public void ContributionsOwnTheirKeysWithoutMutatingTheCaller()
    {
        MetaAttribute first = new() { argumentMap = new() { ["shared"] = 1 } };
        MetaAttribute second = new() { argumentMap = new() { ["shared"] = 2, ["new"] = "original" } };
        MetaAttributeMap map = new();
        map.Append(first);
        map.Append(second);
        Assert.Equal(2, second.argumentMap["shared"]);
        first.argumentMap.Clear();
        second.argumentMap["new"] = "changed";
        Assert.Equal(1, map.QueryArgument("shared"));
        Assert.Equal("original", map.QueryArgument("new"));
        Assert.Null(map.QueryArgument("NEW"));
        Assert.False(map.isNull);
    }

    [Fact]
    public void InvalidScalarConversionUsesTheDefaultWhileUnrelatedErrorsPropagate()
    {
        MetaAttributeMap map = new();
        map.Append(new() { argumentMap = new() { ["bad"] = "invalid", ["overflow"] = long.MaxValue, ["failure"] = new BrokenDisplay() } });
        Assert.True(map.QueryArgumentAsBool("bad", true));
        Assert.Equal(7, map.QueryArgumentAsInteger("overflow", 7));
        Assert.Equal("default", map.QueryArgumentAsString("missing", "default"));
        Assert.Throws<InvalidOperationException>(() => map.QueryArgumentAsString("failure", "default"));
    }

    [Fact]
    public void ArgumentAndTemplateFormattingAreInvariant()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo custom = (CultureInfo)CultureInfo.GetCultureInfo("fr-FR").Clone();
            custom.NumberFormat.NegativeSign = "NEG";
            CultureInfo.CurrentCulture = custom;
            MetaAttributeMap map = new();
            map.Append(new() { argumentMap = new() { ["decimal"] = 1.5, ["negative"] = "-7" } });
            Assert.Equal("1.5", map.QueryArgumentAsString("decimal", ""));
            Assert.Equal(-7, map.QueryArgumentAsInteger("negative", 0));
            CppTemplateArgument argument = new(CXCursor.Null, CppPrimitiveType.@int, -7L);
            Assert.Equal("-7", argument.argString);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void EnvelopeParsingRetainsSeparatorsInsideQuotedArguments()
    {
        MetaAttribute? attribute = CustomAttributeTool.ParseMetaStringFor(
            "rmeta____class____feature|description=\"left|right____suffix\"",
            "class",
            out string? error);
        Assert.Null(error);
        Assert.NotNull(attribute);
        Assert.Equal("feature", attribute.featureName);
        Assert.Equal("left|right____suffix", attribute.argumentMap["description"]);
        Assert.Null(CustomAttributeTool.ParseMetaStringFor("rmeta____class____feature", "field", out error));
        Assert.Null(error);
    }

    [Fact]
    public void MalformedArgumentsReportUsefulDiagnosticsAndPreserveTheDestination()
    {
        Dictionary<string, object> destination = new() { ["existing"] = 1 };
        Assert.False(NamedParameterParser.ParseNamedParameters("flag=true, invalid=", destination, out string? error));
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.DoesNotContain("System.Collections", error);
        Assert.Single(destination);
        Assert.True(NamedParameterParser.ParseNamedParameters("flag=true, flag=false, count=3", destination, out error));
        Assert.Null(error);
        Assert.Equal(true, destination["flag"]);
        Assert.Equal(3, destination["count"]);
    }

    private sealed class BrokenDisplay
    {
        public override string ToString() => throw new InvalidOperationException("Custom conversion failed.");
    }
}
