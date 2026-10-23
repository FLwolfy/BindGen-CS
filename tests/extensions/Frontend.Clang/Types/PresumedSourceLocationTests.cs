using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.Targets.Desktop.Targeting;
using Xunit;

namespace BGCS.Frontend.Clang.Tests;

public sealed class PresumedSourceLocationTests : FrontendTestHost
{
    [Theory]
    [InlineData(SourceLanguage.C)]
    [InlineData(SourceLanguage.Cpp)]
    public async Task EffectiveSourcePreservesOriginalPositionsSystemClassificationAndLogicalIdentity(SourceLanguage language)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-presumed-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string original = Path.Combine(root, "original.h"), sdk = Path.Combine(root, "sdk.h");
            await File.WriteAllTextAsync(original, "/* Original native compiler input. */");
            await File.WriteAllTextAsync(sdk, "/* Original SDK input. */");
            string text = "# 30 \"" + original.Replace('\\', '/') + "\"\n" + """
                /** Helper documentation survives native preprocessing. */
                static inline int Local(int value) { return value + 1; }
                struct Outer { struct { int nested_value; } nested; };
                """ + "\n# 70 \"" + sdk.Replace('\\', '/') + "\" 3\nint SdkOnly(int value);\n"
                + "# 90 \"" + original.Replace('\\', '/') + "\"\nint App(int value);\n";
            string first = Path.Combine(root, "first.i"), second = Path.Combine(root, "a-longer-effective-input.i");
            await File.WriteAllTextAsync(first, text);
            await File.WriteAllTextAsync(second, "\n\n\n\n\n" + text);
            NativeModule initial = await Parse(first), moved = await Parse(second);
            Assert.DoesNotContain(initial.diagnostics, diagnostic => diagnostic.isFailure);
            NativeDeclaration local = Assert.Single(initial.declarations, declaration => declaration.kind == "Function" && declaration.name == "Local");
            Assert.Equal("include/original.h", local.source.file); Assert.Equal(31, local.source.line);
            Assert.Contains("Helper documentation", local.documentation);
            NativeDeclaration app = Assert.Single(initial.declarations, declaration => declaration.kind == "Function" && declaration.name == "App");
            Assert.Equal(90, app.source.line); Assert.False(app.isSystemDeclaration);
            Assert.DoesNotContain(initial.declarations, declaration => declaration.name == "SdkOnly");
            Assert.Equal(JsonSerializer.Serialize(initial), JsonSerializer.Serialize(moved));
            Assert.DoesNotContain("first.i", JsonSerializer.Serialize(initial));
            Assert.DoesNotContain("a-longer-effective-input.i", JsonSerializer.Serialize(moved));

            async Task<NativeModule> Parse(string aggregate)
            {
                FrontendRequest request = new(new TargetResolver().Resolve(new(new("host"))), language, [aggregate])
                {
                    pathBase = root,
                    parseMacros = false,
                    parseComments = true,
                    usePresumedSourceLocations = true,
                    sourcePathMappings = ImmutableDictionary<string, string>.Empty.Add(aggregate, "effective/module.h")
                        .Add(original, "include/original.h").Add(sdk, "sdk/original.h")
                };
                return (await new ClangFrontend(host).ParseAsync(request, CancellationToken.None)).module;
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task EffectiveSourceDiagnosticsUseOriginalPositionsWithoutChangingPhysicalInputSnapshots()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-presumed-error-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string original = Path.Combine(root, "original.h"), aggregate = Path.Combine(root, "effective.i");
            await File.WriteAllTextAsync(original, "/* Original native compiler input. */");
            await File.WriteAllTextAsync(aggregate, "# 73 \"" + original.Replace('\\', '/') + "\"\nint broken( ;\n");
            var result = await new ClangFrontend(host).ParseAsync(new(new TargetResolver().Resolve(new(new("host"))), SourceLanguage.C, [aggregate])
            {
                pathBase = root,
                parseMacros = false,
                usePresumedSourceLocations = true,
                sourcePathMappings = ImmutableDictionary<string, string>.Empty.Add(aggregate, "effective/module.h").Add(original, "include/original.h")
            }, CancellationToken.None);
            Assert.Contains(result.module.diagnostics, diagnostic => diagnostic.isFailure && diagnostic.location is { file: "include/original.h", line: 73 });
            Assert.Contains(result.snapshot.files, file => Path.GetFullPath(file.path) == aggregate);
            Assert.DoesNotContain(result.snapshot.files, file => Path.GetFullPath(file.path) == original);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
