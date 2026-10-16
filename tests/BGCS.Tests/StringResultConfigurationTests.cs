using System;
using System.IO;
using BGCS.Configuration;
using BGCS.Contracts.Interop;
using Xunit;

namespace BGCS.Tests;

public sealed class StringResultConfigurationTests
{
    [Theory]
    [InlineData("zero-cap")]
    [InlineData("negative-cap")]
    [InlineData("zero-width")]
    [InlineData("negative-fixed")]
    [InlineData("terminator-fixed")]
    [InlineData("terminator-parameter")]
    [InlineData("fixed-parameter")]
    [InlineData("direction-without-parameter")]
    [InlineData("retention-without-parameter")]
    [InlineData("undefined-encoding")]
    [InlineData("duplicate")]
    public void AmbiguousOrInvalidNativeExtentsFailBeforeAnyParsing(string fault)
    {
        StringResultConfiguration rule = Rule();
        rule = fault switch
        {
            "zero-cap" => rule with { maximumCodeUnits = 0 },
            "negative-cap" => rule with { maximumCodeUnits = -1 },
            "zero-width" => rule with { codeUnitWidth = 0 },
            "negative-fixed" => rule with { zeroTerminated = false, fixedLength = -1 },
            "terminator-fixed" => rule with { fixedLength = 2 },
            "terminator-parameter" => rule with { lengthParameter = "count" },
            "fixed-parameter" => rule with { zeroTerminated = false, fixedLength = 2, lengthParameter = "count" },
            "direction-without-parameter" => rule with { lengthDirection = NativeParameterDirection.Output },
            "retention-without-parameter" => rule with { lengthOutputCallBounded = true },
            "undefined-encoding" => rule with { encoding = (NativeTextEncoding)999 },
            _ => rule
        };
        BindingConfiguration configuration = Configuration() with { semantics = new() { stringResults = fault == "duplicate" ? [rule, rule] : [rule] } };
        Assert.Throws<ArgumentException>(() => new GenerationRequest(configuration, Path.GetTempPath()));
    }

    [Fact]
    public void SimpleExplicitTextContractDefaultsOnlyEncodingWidthAndScanCap()
    {
        StringResultConfiguration rule = Rule();
        BindingConfiguration configuration = Configuration() with { semantics = new() { stringResults = [rule] } };
        _ = new GenerationRequest(configuration, Path.GetTempPath());
        Assert.Null(rule.codeUnitWidth);
        Assert.Equal(1048576, rule.maximumCodeUnits);
        Assert.Null(rule.fixedLength);
        Assert.Null(rule.lengthParameter);
        Assert.Equal(NativeTextByteOrder.Native, rule.byteOrder);
    }

    private static StringResultConfiguration Rule() => new() { function = "get_text", encoding = NativeTextEncoding.Utf8, zeroTerminated = true, allowsNull = false };
    private static BindingConfiguration Configuration() => new()
    { schemaVersion = 1, library = new() { id = "text", nativeName = "text" }, source = new() { headers = ["native.h"] } };
}
