using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.Contracts.Requests;
using BGCS.Platforms.Targeting;
using Xunit;
using Xunit.Abstractions;

namespace BGCS.Frontend.Clang.Tests;

public sealed class GeneratedSourceIdentityTests(ITestOutputHelper output)
{
    [Fact]
    public async Task MappedGeneratedFiles_PreserveAnonymousAndFileScopedIdentitiesAcrossPhysicalStores()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-generated-identities-" + Guid.NewGuid().ToString("N"));
        string first = Path.Combine(root, "first", "one.hpp"), second = Path.Combine(root, "different-store-length", "two.hpp");
        Directory.CreateDirectory(Path.GetDirectoryName(first)!);
        Directory.CreateDirectory(Path.GetDirectoryName(second)!);
        try
        {
            const string source = """
                typedef struct { int value; } Payload;
                struct Outer { struct { int nested_value; } nested; };
                static int Internal(int value);
                inline Payload Public(Payload value) { return value; }
                """;
            await File.WriteAllTextAsync(first, source);
            await File.WriteAllTextAsync(second, source);
            NativeModule firstModule = await Parse(first), secondModule = await Parse(second);

            Assert.DoesNotContain(firstModule.diagnostics, diagnostic => diagnostic.isFailure);
            Assert.DoesNotContain(secondModule.diagnostics, diagnostic => diagnostic.isFailure);
            string firstJson = JsonSerializer.Serialize(firstModule), secondJson = JsonSerializer.Serialize(secondModule);
            if (firstJson != secondJson)
            {
                output.WriteLine("First generated source model: " + firstJson);
                output.WriteLine("Second generated source model: " + secondJson);
            }
            Assert.Equal(firstJson, secondJson);
            Assert.Contains(firstModule.declarations, declaration => declaration.name == "Internal");
            Assert.Contains(firstModule.types, type => type.category == NativeTypeCategory.Record && type.spelling.Contains("unnamed", StringComparison.Ordinal));
            Assert.All(firstModule.declarations.Where(declaration => declaration.source.file.Length != 0),
                declaration => Assert.Equal("contributions/fixture.generated/public.hpp", declaration.source.file));

            async Task<NativeModule> Parse(string header) => (await new ClangFrontend().ParseAsync(new(new TargetResolver().Resolve(new(new("host"))), SourceLanguage.Cpp, [header])
            {
                pathBase = root,
                parseMacros = false,
                sourcePathMappings = ImmutableDictionary<string, string>.Empty.Add(header, "contributions/fixture.generated/public.hpp")
            }, CancellationToken.None)).module;
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task GeneratedDiagnostic_UsesDeclaredLogicalSourceWithoutRewritingItsMessageLiteral()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-generated-diagnostics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "generated.hpp");
            await File.WriteAllTextAsync(header, "#error keep_this_literal\n");
            NativeModule module = (await new ClangFrontend().ParseAsync(new(new TargetResolver().Resolve(new(new("host"))), SourceLanguage.Cpp, [header])
            {
                pathBase = root,
                parseMacros = false,
                sourcePathMappings = ImmutableDictionary<string, string>.Empty.Add(header, "contributions/fixture.generated/error.hpp")
            }, CancellationToken.None)).module;

            var failure = Assert.Single(module.diagnostics, diagnostic => diagnostic.isFailure);
            Assert.Equal("contributions/fixture.generated/error.hpp", failure.location!.Value.file);
            Assert.Contains("keep_this_literal", failure.message);
            Assert.DoesNotContain(root, failure.message, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
