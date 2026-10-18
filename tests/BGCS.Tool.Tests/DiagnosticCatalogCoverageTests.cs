using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using BGCS.Contracts;
using Xunit;

namespace BGCS.Tool.Tests;

public sealed class DiagnosticCatalogCoverageTests
{
    [Fact]
    public void EveryProductionDiagnosticIdHasAnExecutableExplanation()
    {
        string root = Repository();
        Regex literal = new("\"(BGCS[A-Z0-9-]*)\"");
        Regex diagnostic = new("^(?:BGCS(?:-[A-Z0-9]+)+|BGCS(?:CS|CPP|IO)[0-9]{3}|BGCSCPP-INSTANTIATION)$");
        HashSet<string> emitted = new(StringComparer.Ordinal);
        foreach (string directory in new[] { "src", "integrations" })
            foreach (string path in Directory.EnumerateFiles(Path.Combine(root, directory), "*.cs", SearchOption.AllDirectories))
            {
                if (Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj")
                    || Path.GetFileName(path) == "BindingDiagnosticCatalog.Generated.cs") continue;
                foreach (Match match in literal.Matches(File.ReadAllText(path)))
                    if (diagnostic.IsMatch(match.Groups[1].Value)) emitted.Add(match.Groups[1].Value);
            }
        Assert.NotEmpty(emitted);
        foreach (string code in emitted)
            Assert.True(BindingDiagnosticCatalog.TryGet(code, out _), "Production diagnostic has no explain entry: " + code);
    }

    [Fact]
    public void RuntimeCatalogMatchesTheReviewedBilingualSource()
    {
        using JsonDocument source = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repository(), "eng", "diagnostics", "catalog.json")));
        JsonElement[] rows = source.RootElement.GetProperty("diagnostics").EnumerateArray().ToArray();
        Assert.Equal(rows.Length, BindingDiagnosticCatalog.all.Count);
        Assert.Equal(rows.Select(row => row.GetProperty("code").GetString()).Order(StringComparer.Ordinal), BindingDiagnosticCatalog.all.Select(row => row.code));
        foreach (JsonElement row in rows)
        {
            string code = row.GetProperty("code").GetString()!;
            Assert.True(BindingDiagnosticCatalog.TryGet(" " + code.ToLowerInvariant() + " ", out BindingDiagnosticDescriptor descriptor));
            Assert.Equal(row.GetProperty("title").GetString(), descriptor.title);
            Assert.Equal(row.GetProperty("cause").GetString(), descriptor.cause);
            Assert.Equal(row.GetProperty("resolution").GetString(), descriptor.resolution);
            foreach (string property in new[] { "title", "cause", "resolution" })
                Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("zh").GetProperty(property).GetString()));
        }
    }

    [Theory]
    [InlineData("BGCS-GENERATION-SESSION")]
    [InlineData("BGCS-DOCTOR-WORKER")]
    [InlineData("BGCS-CACHE-MISS")]
    [InlineData("BGCS-FRIENDLY-OMITTED")]
    public void ExplainResolvesNewPipelineStagesAsStructuredOutput(string code)
    {
        using StringWriter output = new();
        using StringWriter error = new();
        Assert.Equal(0, CliInvocation.Run(["explain", code, "--json"], Environment.CurrentDirectory, output, error));
        using JsonDocument result = JsonDocument.Parse(output.ToString());
        Assert.Equal(code, result.RootElement.GetProperty("code").GetString());
        Assert.True(BindingDiagnosticCatalog.TryGet(code, out BindingDiagnosticDescriptor expected));
        Assert.Equal(expected.cause, result.RootElement.GetProperty("cause").GetString());
        Assert.Equal(expected.resolution, result.RootElement.GetProperty("resolution").GetString());
        Assert.Equal(string.Empty, error.ToString());
    }

    private static string Repository()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("The diagnostic source coverage fixture requires the repository.");
    }
}
