using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace BGCS.Tests;

public sealed class CandidateMetadataNormalizerTests
{
    private const string C_LOGICAL = "C:\\Public Output\\Generated";
    private static readonly string[] s_origins = ["C:/Private", "../.bgcs-staging"];
    private const string C_SOURCE = """
        {
          "unchanged": "\u0061\u00e9\u263a \"quotes\" \\ backslash \ud83d\ude00",
          "path": "C:\\Private\\Api\\Value.cs",
          "root": "C:/Private/",
          "casePath": "c:/private/Api/value.cs",
          "relative": "../.bgcs-staging/metadata/value.json",
          "prefix": "prefix C:/Private/value",
          "sibling": "C:/PrivateSibling/value",
          "preview": "C:\\Preview\\\\",
          "previewChild": "C:/Preview/child",
          "outside": "C:\\External\\raw",
          "null": null,
          "number": 42,
          "boolean": true,
          "nested": {
            "value": "C:/Private/Unicode/\u03bb\u0022file.cs",
            "array": [null, false, 17, "plain", "C:/Private//double", ["../.bgcs-staging/deep", {"path":"C:/Private/object"}]],
            "excludedDirectories": ["C:/Private/native", "../.bgcs-staging/native", "C:\\Private\\native", "PATH", "path"],
            "ExcludedDirectories": ["C:/Private/native", "../.bgcs-staging/native"]
          },
          "excludedDirectories": ["C:/Private", "../.bgcs-staging", "C:/Private/", "C:\\Outside", "C:/Outside"]
        }
        """;

    [Theory]
    [InlineData(false, false, "parsed")]
    [InlineData(false, true, "parsed")]
    [InlineData(true, false, "parsed")]
    [InlineData(true, true, "parsed")]
    [InlineData(false, false, "serialized")]
    [InlineData(false, true, "serialized")]
    [InlineData(true, false, "serialized")]
    [InlineData(true, true, "serialized")]
    [InlineData(false, false, "relaxed")]
    [InlineData(false, true, "relaxed")]
    [InlineData(true, false, "relaxed")]
    [InlineData(true, true, "relaxed")]
    public void ExactWrittenBytesMatchThePreviousAlgorithmForAllNestedValueAndEncoderOrigins(bool ignoreCase, bool preview, string origin)
    {
        JsonNode? actual = Source(origin), expected = Source(origin);
        string? previewOutput = preview ? "C:/Preview" : null;
        new Normalizer(s_origins, C_LOGICAL, previewOutput, ignoreCase).Apply(actual);
        Previous(expected, s_origins, C_LOGICAL, previewOutput, ignoreCase);
        Assert.Equal(Written(expected), Written(actual));
        Assert.Equal(Written(expected), new Normalizer(s_origins, C_LOGICAL, previewOutput, ignoreCase).Write(Element(origin)));
    }

    [Fact]
    public void UnchangedObjectAndArrayStringsRetainTheirOriginalNodes()
    {
        JsonObject document = new()
        {
            ["plain"] = "Unicode é 😀 \"quote\" C:\\External\\raw",
            ["path"] = "C:/Private/value",
            ["nested"] = new JsonObject { ["plain"] = "untouched", ["path"] = "../.bgcs-staging/nested" },
            ["array"] = new JsonArray("plain", "C:/Private/array")
        };
        JsonNode? plain = document["plain"], nested = document["nested"], nestedPlain = nested!["plain"], array = document["array"], arrayPlain = array![0];
        new Normalizer(s_origins, C_LOGICAL, null, false).Apply(document);
        Assert.Same(plain, document["plain"]);
        Assert.Same(nested, document["nested"]);
        Assert.Same(nestedPlain, document["nested"]!["plain"]);
        Assert.Same(array, document["array"]);
        Assert.Same(arrayPlain, document["array"]![0]);
        Assert.Equal("artifact:/value", document["path"]!.GetValue<string>());
        Assert.Equal("artifact:/nested", document["nested"]!["path"]!.GetValue<string>());
        Assert.Equal("artifact:/array", document["array"]![1]!.GetValue<string>());
    }

    [Fact]
    public void ParsedElementStringsAreRetainedWhileTheDefaultWriterStillUsesIdenticalEscaping()
    {
        JsonNode actual = Source("parsed")!, expected = Source("parsed")!;
        JsonNode? original = actual["unchanged"], outside = actual["outside"];
        new Normalizer(s_origins, C_LOGICAL, null, false).Apply(actual);
        Previous(expected, s_origins, C_LOGICAL, null, false);
        Assert.Same(original, actual["unchanged"]);
        Assert.Same(outside, actual["outside"]);
        Assert.Equal(Written(expected), Written(actual));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CandidateOriginPrecedenceIsFrozenWithoutSortingOrBroadeningThePrefix(bool reverse)
    {
        string[] origins = reverse ? ["C:/Private/Nested", "C:/Private"] : ["C:/Private", "C:/Private/Nested"];
        Normalizer normalizer = new(origins, C_LOGICAL, null, false);
        origins[0] = "C:/Different";
        JsonObject document = new() { ["path"] = "C:/Private/Nested/value", ["sibling"] = "C:/PrivateSibling/value" };
        normalizer.Apply(document);
        Assert.Equal(reverse ? "artifact:/value" : "artifact:/Nested/value", document["path"]!.GetValue<string>());
        Assert.Equal("C:/PrivateSibling/value", document["sibling"]!.GetValue<string>());
    }

    [Fact]
    public void PreviewRootMappingPrecedesAnOverlappingCandidateOriginAndKeepsTheLogicalSpelling()
    {
        JsonObject document = new() { ["root"] = "c:\\preview\\\\", ["child"] = "C:/Preview/child" };
        new Normalizer(["C:/Preview"], C_LOGICAL, "C:/Preview", true).Apply(document);
        Assert.Equal(C_LOGICAL, document["root"]!.GetValue<string>());
        Assert.Equal("artifact:/child", document["child"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExclusionDeduplicationRunsAfterChildMappingAndPreservesFirstOccurrence(bool ignoreCase)
    {
        JsonObject document = new() { ["excludedDirectories"] = new JsonArray("C:/Private/native", "../.bgcs-staging/native", "PATH", "path", "PATH") };
        new Normalizer(s_origins, C_LOGICAL, null, ignoreCase).Apply(document);
        string[] actual = document["excludedDirectories"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray();
        string[] expected = ignoreCase ? ["artifact:/native", "PATH"] : ["artifact:/native", "PATH", "path"];
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"C:/Private/root\"")]
    [InlineData("42")]
    [InlineData("[null,[\"C:/Private/deep\"],{\"value\":null}]")]
    public void NullAndScalarRootsAndNestedNullValuesKeepTheOriginalRules(string json)
    {
        JsonNode? actual = JsonNode.Parse(json), expected = JsonNode.Parse(json);
        new Normalizer(s_origins, C_LOGICAL, null, false).Apply(actual);
        Previous(expected, s_origins, C_LOGICAL, null, false);
        Assert.Equal(Written(expected), Written(actual));
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(Written(expected), new Normalizer(s_origins, C_LOGICAL, null, false).Write(document.RootElement));
    }

    [Theory]
    [InlineData("[null]")]
    [InlineData("[42]")]
    [InlineData("[[\"C:/Private/path\"]]")]
    [InlineData("[false]")]
    [InlineData("[{}]")]
    [InlineData("[42,{\"excludedDirectories\":[null]}]")]
    [InlineData("[{\"excludedDirectories\":[42]},null]")]
    public void MalformedExclusionsRetainTheExistingFailureInsteadOfBeingIgnored(string exclusions)
    {
        string json = "{\"value\":\"C:/Private/changed\",\"excludedDirectories\":" + exclusions + "}";
        JsonNode? actual = JsonNode.Parse(json), expected = JsonNode.Parse(json);
        Exception? oldFailure = Record.Exception(() => Previous(expected, s_origins, C_LOGICAL, null, false));
        Exception? newFailure = Record.Exception(() => new Normalizer(s_origins, C_LOGICAL, null, false).Apply(actual));
        Assert.NotNull(oldFailure);
        Assert.NotNull(newFailure);
        Assert.Equal(oldFailure.GetType(), newFailure.GetType());
        using JsonDocument document = JsonDocument.Parse(json);
        Exception? streamingFailure = Record.Exception(() => new Normalizer(s_origins, C_LOGICAL, null, false).Write(document.RootElement));
        Assert.NotNull(streamingFailure);
        Assert.Equal(oldFailure.GetType(), streamingFailure.GetType());
    }

    [Theory]
    [InlineData("{\"same\":1,\"same\":2}")]
    [InlineData("{\"same\":1,\"\\u0073ame\":2}")]
    [InlineData("{\"before\":{\"excludedDirectories\":[null]},\"same\":1,\"same\":2}")]
    [InlineData("{\"before\":{\"excludedDirectories\":[null]},\"after\":{\"same\":1,\"same\":2}}")]
    [InlineData("{\"before\":{\"same\":1,\"same\":2},\"after\":{\"excludedDirectories\":[null]}}")]
    public void StreamingRetainsDuplicatePropertyAndNestedNormalizationFailurePrecedence(string json)
    {
        JsonNode? expected = JsonNode.Parse(json);
        Exception? oldFailure = Record.Exception(() => Previous(expected, s_origins, C_LOGICAL, null, false));
        using JsonDocument document = JsonDocument.Parse(json);
        Exception? streamingFailure = Record.Exception(() => new Normalizer(s_origins, C_LOGICAL, null, false).Write(document.RootElement));
        Assert.NotNull(oldFailure);
        Assert.NotNull(streamingFailure);
        Assert.Equal(oldFailure.GetType(), streamingFailure.GetType());
    }

    [Theory]
    [InlineData(63, false)]
    [InlineData(64, false)]
    [InlineData(65, false)]
    [InlineData(65, true)]
    public void StreamingUsesTheSameEffectiveWriterDepthAndNormalizationBeforeWriteFailure(int depth, bool malformed)
    {
        string nested = new string('[', depth) + "0" + new string(']', depth);
        string json = malformed ? "{\"deep\":" + nested + ",\"excludedDirectories\":[null]}" : nested;
        JsonNode? expected = JsonNode.Parse(json, documentOptions: new() { MaxDepth = 128 });
        byte[]? expectedBytes = null, actualBytes = null;
        Exception? oldFailure = Record.Exception(() =>
        {
            Previous(expected, s_origins, C_LOGICAL, null, false);
            expectedBytes = Written(expected);
        });
        using JsonDocument document = JsonDocument.Parse(json, new() { MaxDepth = 128 });
        Exception? streamingFailure = Record.Exception(() => actualBytes = new Normalizer(s_origins, C_LOGICAL, null, false).Write(document.RootElement));
        Assert.Equal(oldFailure?.GetType(), streamingFailure?.GetType());
        if (oldFailure is null) Assert.Equal(expectedBytes, actualBytes);
        else Assert.Null(actualBytes);
        if (malformed) Assert.IsType<NullReferenceException>(streamingFailure);
    }

    [Fact]
    public void StreamingPreservesPropertyOrderNumericLexemesAndOnlyDeduplicatesTheExactExclusionName()
    {
        const string json = "{\"z\":1e+03,\"A\":1.2300,\"a\":-0,\"fraction\":-0.0,\"large\":1e9999,\"excludedDirectories\":[\"C:/Private/native\",\"../.bgcs-staging/native\"],\"ExcludedDirectories\":[\"C:/Private/native\",\"../.bgcs-staging/native\"]}";
        JsonNode? expected = JsonNode.Parse(json);
        Previous(expected, s_origins, C_LOGICAL, null, false);
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(Written(expected), new Normalizer(s_origins, C_LOGICAL, null, false).Write(document.RootElement));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StreamingFreezesOriginOrderAndPreviewPriority(bool reverse)
    {
        string[] origins = reverse ? ["C:/Private/Nested", "C:/Private"] : ["C:/Private", "C:/Private/Nested"];
        string[] frozen = origins.ToArray();
        Normalizer normalizer = new(origins, C_LOGICAL, "C:/Private/Nested", true);
        origins[0] = "C:/Different";
        const string json = "{\"preview\":\"c:\\\\private\\\\Nested\\\\\\\\\",\"child\":\"C:/Private/Nested/value\",\"sibling\":\"C:/PrivateSibling/value\"}";
        JsonNode? expected = JsonNode.Parse(json);
        Previous(expected, frozen, C_LOGICAL, "C:/Private/Nested", true);
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(Written(expected), normalizer.Write(document.RootElement));
    }

    [Fact]
    public async Task StreamingASecondDocumentAndConcurrentDocumentsDoesNotReuseMutableState()
    {
        Normalizer normalizer = new(s_origins, C_LOGICAL, "C:/Preview", true);
        JsonNode? expected = Source("serialized");
        Previous(expected, s_origins, C_LOGICAL, "C:/Preview", true);
        byte[] expectedBytes = Written(expected);
        using JsonDocument empty = JsonDocument.Parse("{\"excludedDirectories\":[]}");
        _ = normalizer.Write(empty.RootElement);
        byte[][] results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => normalizer.Write(Element("serialized")))));
        Assert.All(results, actual => Assert.Equal(expectedBytes, actual));
    }

    [Fact]
    public void StreamingSupportsTheActualWorkerManifestSerializerOptionsAndConverters()
    {
        var value = new { kind = BGCS.Contracts.Inputs.SourceLanguage.Cpp, path = "C:/Private/native", excludedDirectories = new[] { "C:/Private", "../.bgcs-staging" } };
        JsonSerializerOptions options = BGCS.Infrastructure.Workers.WorkerProtocol.jsonOptions;
        JsonNode? expected = JsonSerializer.SerializeToNode(value, options);
        Previous(expected, s_origins, C_LOGICAL, null, false);
        JsonElement document = JsonSerializer.SerializeToElement(value, options);
        Assert.Equal(Written(expected), new Normalizer(s_origins, C_LOGICAL, null, false).Write(document));
    }

    [Fact]
    public async Task OneFrozenNormalizerCanProcessIndependentDocumentsConcurrently()
    {
        Normalizer normalizer = new(s_origins, C_LOGICAL, "C:/Preview", false);
        JsonNode? expected = Source("serialized");
        Previous(expected, s_origins, C_LOGICAL, "C:/Preview", false);
        byte[] bytes = Written(expected);
        byte[][] results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            JsonNode? document = Source("serialized");
            normalizer.Apply(document);
            return Written(document);
        })));
        Assert.All(results, actual => Assert.Equal(bytes, actual));
    }

    private static JsonNode? Source(string origin)
        => origin == "parsed" ? JsonNode.Parse(C_SOURCE) : JsonSerializer.SerializeToNode(JsonSerializer.Deserialize<Dictionary<string, object?>>(C_SOURCE),
            origin == "relaxed" ? new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping } : null);

    private static JsonElement Element(string origin)
    {
        if (origin != "parsed") return JsonSerializer.SerializeToElement(JsonSerializer.Deserialize<Dictionary<string, object?>>(C_SOURCE),
            origin == "relaxed" ? new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping } : null);
        using JsonDocument document = JsonDocument.Parse(C_SOURCE);
        return document.RootElement.Clone();
    }

    private static byte[] Written(JsonNode? document)
        => Encoding.UTF8.GetBytes((document?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null") + "\n");

    // Independent verbatim algorithm oracle retained from the original pipeline.
    // It deliberately replaces every string and snapshots every parent's keys.
    private static void Previous(JsonNode? node, string[] origins, string logicalOutput, string? previewOutput, bool ignoreCase)
    {
        if (node is JsonObject properties)
            foreach (string name in properties.Select(property => property.Key).ToArray())
            {
                if (properties[name] is JsonValue scalar && scalar.TryGetValue(out string? text)) properties[name] = Logical(text);
                else Previous(properties[name], origins, logicalOutput, previewOutput, ignoreCase);
                if (name == "excludedDirectories" && properties[name] is JsonArray excluded)
                    properties[name] = new JsonArray(excluded.Select(value => value!.GetValue<string>()).Distinct(ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
                        .Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
            }
        else if (node is JsonArray values)
            for (int index = 0; index < values.Count; index++)
            {
                if (values[index] is JsonValue scalar && scalar.TryGetValue(out string? text)) values[index] = Logical(text);
                else Previous(values[index], origins, logicalOutput, previewOutput, ignoreCase);
            }

        string? Logical(string? text)
        {
            if (text is null) return null;
            string candidate = text.Replace('\\', '/');
            StringComparison comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (previewOutput is not null && candidate.TrimEnd('/').Equals(previewOutput, comparison)) return logicalOutput;
            foreach (string normalized in origins)
            {
                if (candidate.Equals(normalized, comparison)) return "artifact:/";
                if (candidate.StartsWith(normalized + "/", comparison)) return "artifact:/" + candidate[(normalized.Length + 1)..];
            }
            return text;
        }
    }

    private sealed class Normalizer
    {
        private static readonly Type s_type = typeof(BindingHost).Assembly.GetType("BGCS.CandidateMetadataNormalizer", throwOnError: true)!;
        private static readonly ConstructorInfo s_constructor = s_type.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        private static readonly MethodInfo s_normalize = s_type.GetMethod("Normalize", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private static readonly MethodInfo s_write = s_type.GetMethod("WriteNormalized", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private readonly object m_value;
        internal Normalizer(IEnumerable<string> origins, string logicalOutput, string? previewOutput, bool ignoreCase)
            => m_value = s_constructor.Invoke([origins, logicalOutput, previewOutput, ignoreCase]);
        internal void Apply(JsonNode? document)
        {
            try { s_normalize.Invoke(m_value, [document]); }
            catch (TargetInvocationException failure) when (failure.InnerException is not null)
            { ExceptionDispatchInfo.Capture(failure.InnerException).Throw(); throw; }
        }
        internal byte[] Write(JsonElement document)
        {
            using MemoryStream bytes = new();
            using (Utf8JsonWriter writer = new(bytes, new() { Indented = true, MaxDepth = 64 }))
            {
                try { s_write.Invoke(m_value, [document, writer]); }
                catch (TargetInvocationException failure) when (failure.InnerException is not null)
                { ExceptionDispatchInfo.Capture(failure.InnerException).Throw(); throw; }
            }
            bytes.WriteByte((byte)'\n');
            return bytes.ToArray();
        }
    }
}
