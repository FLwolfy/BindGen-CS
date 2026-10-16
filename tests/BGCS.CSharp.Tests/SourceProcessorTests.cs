using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Plugins;
using BGCS.Contracts.Requests;
using BGCS.CSharp.Processing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class SourceProcessorTests
{
    [Theory]
    [InlineData("$$/$&/$`/$'/$+/${word}/$1/$0/$_")]
    [InlineData("${01}/$01/$99/${absent}/$x/$")]
    [InlineData("${word}${word}-$2-${0}")]
    public async Task RegexDollarSubstitutionsMatchTheActualInvariantDotNetOracle(string replacement)
    {
        const string input = "A ab B ac C";
        const string pattern = "(?<word>a)(b|c)";
        Editor editor = new(("Api.cs", input));
        await new RegexArtifactProcessor("oracle", 0, ["**/*.cs"], pattern, replacement).ProcessAsync(editor.Request(), default);
        Assert.Equal(Regex.Replace(input, pattern, replacement, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)), editor.Text("Api.cs"));
    }

    [Fact]
    public async Task FrozenSelectorsUseManagedRelativeCaseSensitivePathsAndLiteralReplacement()
    {
        List<string> paths = ["Types/*.cs"];
        TextArtifactProcessor processor = new("literal", 0, paths, "NAME", "$&", ignoreCase: true);
        paths[0] = "**/*.cs";
        Editor editor = new(("Types/Value.cs", "Name NAME"), ("Types/Nested/Other.cs", "NAME"), ("Api.cs", "NAME"), ("types/Value.cs", "NAME"));
        await processor.ProcessAsync(editor.Request(), default);
        Assert.Equal("$& $&", editor.Text("Types/Value.cs"));
        Assert.Equal("NAME", editor.Text("Types/Nested/Other.cs"));
        Assert.Equal("NAME", editor.Text("Api.cs"));
        Assert.Equal("NAME", editor.Text("types/Value.cs"));
    }

    [Fact]
    public async Task PortableSelectorsMatchThePreviousAnchoredRegexOracle()
    {
        string[] selectors = ["Api.cs", "*", "*.cs", "?.cs", "??.cs", "?pi.cs", "**", "**/*.cs", "Types/**", "Types/**/Value?.cs",
            "**/**/A*.cs", "A/*/**/B?.cs", "Types/*/Other.cs", "[A]+(x).cs", "A/**/B/**", "A/**/**"];
        string[] candidates = ["Api.cs", "api.cs", "Api.txt", "Types/Value1.cs", "Types/Nested/Value2.cs",
            "Types/Nested/Other.cs", "A/B1.cs", "A/X/B2.cs", "A/X/Y/B2.cs", "A/X/Y/B2.txt", "A", "A/",
            "A//B1.cs", "Types//Other.cs", "[A]+(x).cs", "目录/😀.cs", "😀.cs", "A/😀.cs", "A/\n/B1.cs", "A/X/\n", ""];
        foreach (string selector in selectors)
        {
            string[] segments = selector.Split('/');
            StringBuilder expression = new("\\A");
            for (int index = 0; index < segments.Length; index++)
            {
                if (segments[index] == "**") expression.Append(index == segments.Length - 1 ? ".*" : "(?:[^/]+/)*");
                else
                {
                    foreach (char character in segments[index])
                        expression.Append(character switch { '*' => "[^/]*", '?' => "[^/]", _ => Regex.Escape(character.ToString()) });
                    if (index != segments.Length - 1) expression.Append('/');
                }
            }
            expression.Append("\\z");
            Regex oracle = new(expression.ToString(), RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(5));
            foreach (string candidate in candidates)
            {
                Editor editor = new((candidate, "BEFORE"));
                await new TextArtifactProcessor("glob", 0, [selector], "BEFORE", "AFTER", requireMatch: false).ProcessAsync(editor.Request(), default);
                Assert.True(editor.Text(candidate) == (oracle.IsMatch(candidate) ? "AFTER" : "BEFORE"),
                    "Selector: " + selector + "; candidate: " + candidate);
            }
        }
    }

    [Fact]
    public async Task CancelledSelectorEnumerationRejectsEvenWhenNoArtifactMatches()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Editor editor = new(("Api.cs", "BEFORE"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await new TextArtifactProcessor(
            "cancel", 0, ["Types/**"], "BEFORE", "AFTER", requireMatch: false).ProcessAsync(editor.Request(), cancellation.Token));
        Assert.Equal("BEFORE", editor.Text("Api.cs"));
    }

    [Theory]
    [InlineData("../Other.cs")]
    [InlineData("/Other.cs")]
    [InlineData("C:/Other.cs")]
    [InlineData("Types\\Other.cs")]
    [InlineData("Types/**bad.cs")]
    public void TraversalAndInvalidPortableSelectorsFailBeforeProcessing(string path)
        => Assert.Throws<ArgumentException>(() => new TextArtifactProcessor("paths", 0, [path], "a", "b"));

    [Theory]
    [InlineData(0, RegexOptions.None)]
    [InlineData(30001, RegexOptions.None)]
    [InlineData(1000, RegexOptions.RightToLeft)]
    [InlineData(1000, RegexOptions.ECMAScript)]
    public void UnboundedOrUnsupportedRegexPoliciesFailBeforeProcessing(int timeout, RegexOptions options)
        => Assert.Throws<ArgumentException>(() => new RegexArtifactProcessor("regex", 0, ["**/*.cs"], "x", "y", timeout, options));

    [Fact]
    public async Task RegexCaseFoldingIsInvariantAndCancellationDoesNotMutateCandidates()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new("tr-TR");
            Editor editor = new(("Api.cs", "FILE"));
            RegexArtifactProcessor processor = new("culture", 0, ["**/*.cs"], "file", "ok", options: RegexOptions.IgnoreCase);
            await processor.ProcessAsync(editor.Request(), default);
            Assert.Equal("ok", editor.Text("Api.cs"));
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await processor.ProcessAsync(editor.Request(), cancellation.Token));
            Assert.Equal("ok", editor.Text("Api.cs"));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public async Task MissingMatchesAreExplicitAndAdditionalSourceIsParsedAndCollisionChecked()
    {
        Editor editor = new(("Api.cs", "namespace Example; public partial class Api { }"));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await new TextArtifactProcessor("required", 0, ["**/*.cs"], "missing", "x").ProcessAsync(editor.Request(), default));
        await new TextArtifactProcessor("optional", 0, ["**/*.cs"], "missing", "x", requireMatch: false).ProcessAsync(editor.Request(), default);
        AdditionalSourceArtifactProcessor additional = new("add", 0, "Extras/Part.cs", "namespace Example; public partial class Api { public int Extra() => 42; }");
        await additional.ProcessAsync(editor.Request(), default);
        Assert.Contains("42", editor.Text("Extras/Part.cs"), StringComparison.Ordinal);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await new AdditionalSourceArtifactProcessor("collision", 0, "extras/part.cs", "namespace Example; class Other { }").ProcessAsync(editor.Request(), default));
        Assert.Throws<ArgumentException>(() => new AdditionalSourceArtifactProcessor("syntax", 0, "Bad.cs", "class {"));
        Assert.Throws<ArgumentException>(() => new AdditionalSourceArtifactProcessor("top", 0, "Top.cs", "System.Console.WriteLine(42);"));
    }

    [Fact]
    public void EveryFrozenArtifactAffectingRuleChoiceChangesItsFingerprint()
    {
        ICacheFingerprintProvider[] processors =
        [new TextArtifactProcessor("rule", 0, ["**/*.cs"], "a", "b"),
         new TextArtifactProcessor("rule", 0, ["**/*.cs"], "a", "c"),
         new TextArtifactProcessor("rule", 1, ["**/*.cs"], "a", "b"),
         new TextArtifactProcessor("rule", 0, ["Api.cs"], "a", "b"),
         new TextArtifactProcessor("rule", 0, ["**/*.cs"], "a", "b", ignoreCase: true),
         new RegexArtifactProcessor("rule", 0, ["**/*.cs"], "a", "b"),
         new RegexArtifactProcessor("rule", 0, ["**/*.cs"], "a", "b", timeoutMilliseconds: 2),
         new AdditionalSourceArtifactProcessor("rule", 0, "Extra.cs", "class Extra { }")];
        ContentDigest[] values = processors.Select(processor => processor.CaptureFingerprint(null!, default).fingerprint).ToArray();
        Assert.Equal(values.Length, values.Distinct().Count());
        Assert.Equal(values[0], new TextArtifactProcessor("rule", 0, ["**/*.cs"], "a", "b").CaptureFingerprint(null!, default).fingerprint);
    }

    [Fact]
    public async Task AdvancedSyntaxSubclassRewritesDetachedSyntaxAndDoesNotInventCacheIdentity()
    {
        RenameSyntax processor = new();
        Assert.False((object)processor is ICacheFingerprintProvider);
        Editor editor = new(("Api.cs", "namespace Example; public class Before { }"));
        await processor.ProcessAsync(editor.Request(), default);
        Assert.Contains("class After", editor.Text("Api.cs"), StringComparison.Ordinal);
    }

    private sealed class RenameSyntax() : SyntaxArtifactProcessor("syntax", 0, ["**/*.cs"])
    {
        protected override CompilationUnitSyntax Rewrite(string path, CompilationUnitSyntax root, CancellationToken cancellationToken)
        {
            ClassDeclarationSyntax declaration = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
            return root.ReplaceNode(declaration, declaration.WithIdentifier(SyntaxFactory.Identifier("After").WithTriviaFrom(declaration.Identifier)));
        }
    }

    private sealed class Editor : IArtifactEditor
    {
        private const string C_PREFIX = "windows-x64-msvc/managed/";
        private readonly Dictionary<string, string> m_sources = new(StringComparer.Ordinal);
        internal Editor(params (string path, string content)[] sources) { foreach (var source in sources) m_sources.Add(C_PREFIX + source.path, source.content); }
        internal string Text(string path) => m_sources[C_PREFIX + path];
        internal ArtifactProcessingRequest Request() => new(NeutralBackendTests.Model(), m_sources.Select(pair => Describe(pair.Key)).ToImmutableArray(), this);
        private ArtifactDescriptor Describe(string path) => new(path, "text/x-csharp", new(new string('a', 64)));
        public ValueTask<string> ReadTextAsync(string relativePath, CancellationToken cancellationToken) => ValueTask.FromResult(m_sources[relativePath]);
        public ValueTask<ReadOnlyMemory<byte>> ReadBytesAsync(string relativePath, CancellationToken cancellationToken) => ValueTask.FromResult<ReadOnlyMemory<byte>>(Encoding.UTF8.GetBytes(m_sources[relativePath]));
        public ValueTask<ArtifactDescriptor> ReplaceTextAsync(string relativePath, string content, CancellationToken cancellationToken) { m_sources[relativePath] = content; return ValueTask.FromResult(Describe(relativePath)); }
        public ValueTask<ArtifactDescriptor> AddTextAsync(string relativePath, string mediaType, string content, CancellationToken cancellationToken) { m_sources.Add(relativePath, content); return ValueTask.FromResult(Describe(relativePath)); }
        public ValueTask DeleteTextAsync(string relativePath, CancellationToken cancellationToken) { m_sources.Remove(relativePath); return ValueTask.CompletedTask; }
    }
}
