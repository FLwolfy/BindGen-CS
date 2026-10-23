using System;
using System.IO;
using BGCS.Configuration;
using BGCS.Core.Interop;
using Xunit;

namespace BGCS.Tests;

public sealed class CountedStringConfigurationTests
{
    [Theory]
    [InlineData("zero-bound")]
    [InlineData("negative-bound")]
    [InlineData("undefined-direction")]
    [InlineData("direction-without-length")]
    public void InvalidCountedAllocationRulesFailBeforeParsing(string fault)
    {
        StringParameterConfiguration rule = new()
        {
            function = "set_name",
            parameter = "data",
            lengthParameter = "count",
            lengthDirection = NativeParameterDirection.Input
        };
        rule = fault switch
        {
            "zero-bound" => rule with { maximumCodeUnits = 0 },
            "negative-bound" => rule with { maximumCodeUnits = -1 },
            "undefined-direction" => rule with { lengthDirection = (NativeParameterDirection)999 },
            _ => rule with { lengthParameter = null }
        };
        BindingConfiguration configuration = new()
        {
            schemaVersion = 2,
            library = new() { id = "text", nativeName = "text" },
            source = new() { headers = ["native.h"] },
            semantics = new() { stringParameters = [rule] }
        };
        Assert.Throws<ArgumentException>(() => new GenerationRequest(configuration, Path.GetTempPath()));
    }

    [Fact]
    public void StrictJsonAndSchemaPreserveCountedInputChoicesAndDefaults()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-counted-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "bindgen.json");
            File.WriteAllText(path, """
                {"schemaVersion":2,"library":{"id":"text","nativeName":"text"},"source":{"headers":["text.h"]},
                 "semantics":{"stringParameters":[{"function":"set_name","parameter":"data","encoding":"utf8",
                  "codeUnitWidth":1,"lengthUnit":"bytes","direction":"input","lifetime":"call",
                  "zeroTerminated":false,"retainsPointer":false,"allowsNull":true,"lengthParameter":"count",
                  "lengthDirection":"input","maximumCodeUnits":300}]}}
                """);
            StringParameterConfiguration rule = Assert.Single(TestInputs.LoadRequest(path).configuration.semantics.stringParameters);
            Assert.Equal(NativeParameterDirection.Input, rule.lengthDirection);
            Assert.Equal(300, rule.maximumCodeUnits);
            Assert.Equal("count", rule.lengthParameter);
            Assert.Equal(1048576, new StringParameterConfiguration().maximumCodeUnits);
            Assert.Equal(NativeParameterDirection.Unknown, new StringParameterConfiguration().lengthDirection);
            var properties = new SchemaCatalog().CreateBindingSchema()["properties"]!["semantics"]!["properties"]!["stringParameters"]!["items"]!["properties"]!;
            Assert.Equal(1, properties["maximumCodeUnits"]!["minimum"]!.GetValue<int>());
            Assert.Equal(1048576, properties["maximumCodeUnits"]!["default"]!.GetValue<int>());
            Assert.Contains("input", properties["lengthDirection"]!.ToJsonString(), StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
