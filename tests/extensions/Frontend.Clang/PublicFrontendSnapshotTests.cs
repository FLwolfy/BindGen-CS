using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Inputs;
using BGCS.Analysis.Interop;
using BGCS.Core.Requests;
using BGCS.Core.Targets;
using BGCS.Infrastructure.IO;
using BGCS.Targets.Desktop;
using Xunit;

namespace BGCS.Frontend.Clang.Tests;

/// <summary>Retains historical literal source cases through the supported detached frontend contract.</summary>
public sealed class PublicFrontendSnapshotTests : FrontendTestHost
{
    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private static string Inputs => Path.Combine(AppContext.BaseDirectory, "Fixtures", "PublicFacts");
    public static IEnumerable<object[]> Cases() => ReadCases().Select(value => new object[] { value.caseId });
    private static Case[] ReadCases() => JsonSerializer.Deserialize<Case[]>(File.ReadAllText(Path.Combine(Inputs, "cases.json")), s_json)!;

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task DetachedPublicFactsRetainEveryHistoricalSourceCase(string caseId)
    {
        Case input = Assert.Single(ReadCases(), value => value.caseId == caseId);
        string id = input.id, source = input.source, targetId = input.target, sourceSha256 = input.sourceSha256, expected = input.expected;
        string header = Path.Combine(Inputs, source);
        Assert.Equal(sourceSha256, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(header))));
        using JsonDocument provenance = JsonDocument.Parse(File.ReadAllText(Path.Combine(Inputs, "provenance.json")));
        foreach (JsonProperty dependency in provenance.RootElement.GetProperty("fixtureDependencySha256").EnumerateObject())
            Assert.Equal(dependency.Value.GetString(), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(Inputs, dependency.Name)))));
        Assert.True(new DesktopTargetProvider().TryResolve(new(new(targetId), new(compilerPath: "uninstalled-parser-location")) { language = SourceLanguage.Cpp }, out NativeTargetDescriptor? target), id);
        FrontendResult parsed = await new ClangFrontend(host).ParseAsync(new(target!, SourceLanguage.Cpp, [header])
        { pathBase = Inputs, languageStandard = "c++23", parseMacros = true, headerSearchPaths = target!.headerSearchPaths, includeDirectories = [Inputs] }, CancellationToken.None);
        Assert.DoesNotContain(parsed.module.diagnostics, diagnostic => diagnostic.isFailure);
        Assert.DoesNotContain(new NativeModuleVerifier().Validate(parsed.module), diagnostic => diagnostic.isFailure);
        JsonObject actual = JsonSerializer.SerializeToNode(parsed.module, s_json)!.AsObject();
        actual.Remove("modelRevision"); // Wire revisions are intentionally different; this is test-only structural projection.
        JsonNode baseline = JsonNode.Parse(File.ReadAllText(Path.Combine(Inputs, expected)))!;
        Assert.True(JsonNode.DeepEquals(baseline, actual), id + Environment.NewLine + "Expected: " + baseline.ToJsonString(s_json) + Environment.NewLine + "Actual: " + actual.ToJsonString(s_json));
        Assert.True(SourceSnapshotVerifier.Matches(parsed.snapshot, host.paths));
        Assert.NotEmpty(parsed.snapshot.pathSemantics);
    }
    private sealed record Case(
        string caseId,
        string id,
        string source,
        string target,
        string sourceSha256,
        string expected
    );
}
