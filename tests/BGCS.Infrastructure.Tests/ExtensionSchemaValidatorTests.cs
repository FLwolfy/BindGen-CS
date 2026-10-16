using System;
using System.Text.Json;
using BGCS.Infrastructure.Configuration;
using Xunit;

namespace BGCS.Infrastructure.Tests;

public sealed class ExtensionSchemaValidatorTests
{
    [Fact]
    public void BundledDefinitions_ValidateNestedTypesFalseZeroAndConditionalRules()
    {
        using JsonDocument schema = JsonDocument.Parse("""
            { "$schema":"https://json-schema.org/draft/2020-12/schema", "type":"object", "additionalProperties":false,
              "$defs":{"count":{"type":"integer","minimum":0,"maximum":10}},
              "properties":{"enabled":{"type":"boolean"},"count":{"$ref":"#/$defs/count"},"mode":{"enum":["raw","friendly"]}},
              "required":["enabled","count"],"if":{"properties":{"enabled":{"const":true}}},"then":{"required":["mode"]} }
            """);
        ExtensionSchemaValidator validator = new();
        using JsonDocument valid = JsonDocument.Parse("{\"enabled\":false,\"count\":0}");
        validator.Validate(schema.RootElement, valid.RootElement, "fixture");
        foreach (string invalid in new[] { "{\"enabled\":true,\"count\":0}", "{\"enabled\":false,\"count\":11}", "{\"enabled\":false,\"count\":0,\"typo\":true}", "{\"enabled\":false,\"count\":\"0\"}" })
        {
            using JsonDocument input = JsonDocument.Parse(invalid);
            ArgumentException failure = Assert.Throws<ArgumentException>(() => validator.Validate(schema.RootElement, input.RootElement, "fixture"));
            Assert.Contains("fixture", failure.Message);
        }
    }

    [Theory]
    [InlineData("{\"$ref\":\"https://example.invalid/schema.json\"}")]
    [InlineData("{\"$defs\":{\"local\":{\"$ref\":\"relative.json\"}}}")]
    [InlineData("{\"$dynamicRef\":\"other.json#anchor\"}")]
    [InlineData("{\"$schema\":\"https://example.invalid/custom-dialect\"}")]
    [InlineData("{\"properties\":{\"child\":{\"$id\":\"child\",\"type\":\"object\"}}}")]
    public void ExternalReferencesAndDialects_AreRejectedBeforeAnyFetch(string source)
    {
        using JsonDocument schema = JsonDocument.Parse(source);
        Assert.Throws<NotSupportedException>(() => new ExtensionSchemaValidator().ValidateSchema(schema.RootElement));
    }

    [Fact]
    public void AnnotationValues_AreNotMistakenForSchemaResources()
    {
        using JsonDocument schema = JsonDocument.Parse("{\"type\":\"object\",\"default\":{\"$ref\":\"ordinary-user-data\"},\"examples\":[{\"$id\":\"ordinary-user-data\"}]}");
        new ExtensionSchemaValidator().ValidateSchema(schema.RootElement);
    }
}
