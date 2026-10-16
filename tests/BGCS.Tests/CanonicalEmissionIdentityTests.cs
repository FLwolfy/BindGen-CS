using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using BGCS.Contracts.Common;
using Xunit;

namespace BGCS.Tests;

public sealed class CanonicalEmissionIdentityTests
{
    [Theory]
    [InlineData("null")]
    [InlineData("[true,false,null,1e+02,-0.0,18446744073709551615]")]
    [InlineData("{\"z\":1,\"a\":2,\"z\":3,\"a\":{\"z\":0,\"a\":4},\"A\":5}")]
    [InlineData("{\"é\":\"λ😀\\n\\\"\\\\\",\"e\":\"\\u0061\",\"😀\":{},\"𐀀\":[],\"\":0}")]
    [InlineData("[{\"b\":1,\"a\":2},{\"a\":3},{\"x\":{\"c\":4,\"b\":[{\"z\":5,\"a\":6}]},\"a\":7},{}]")]
    public void CanonicalBytesAndStreamingDigestMatchThePreviousStableOrdering(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        AssertIdentity(document.RootElement);
    }

    [Fact]
    public void LargeSiblingModelsAndConcurrentInvocationsDoNotShareSortingState()
    {
        JsonElement model = JsonSerializer.SerializeToElement(Enumerable.Range(0, 10000).Select(index => new
        {
            z = index,
            a = new { third = index % 3, first = "Unicode λ", second = new[] { index, -index } },
            empty = new { }
        }).ToArray());
        Parallel.For(0, 4, _ => AssertIdentity(model));
    }

    private static void AssertIdentity(JsonElement element)
    {
        using MemoryStream expected = new(), actual = new();
        using (Utf8JsonWriter writer = new(expected)) Previous(element, writer);
        Type cache = typeof(BindingHost).Assembly.GetType("BGCS.EmissionStageCache", throwOnError: true)!;
        using (Utf8JsonWriter writer = new(actual))
            cache.GetMethod("Canonical", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [element, writer]);
        Assert.Equal(expected.ToArray(), actual.ToArray());
        ContentDigest digest = (ContentDigest)cache.GetMethod("CanonicalDigest", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [element])!;
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(expected.GetBuffer().AsSpan(0, checked((int)expected.Length)))), digest.value);
    }

    private static void Previous(JsonElement element, Utf8JsonWriter writer)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (JsonProperty property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                Previous(property.Value, writer);
            }
            writer.WriteEndObject();
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (JsonElement item in element.EnumerateArray()) Previous(item, writer);
            writer.WriteEndArray();
        }
        else element.WriteTo(writer);
    }
}
