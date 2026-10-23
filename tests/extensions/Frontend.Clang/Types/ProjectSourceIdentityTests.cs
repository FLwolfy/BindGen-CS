using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.Targets.Desktop.Targeting;
using Xunit;

namespace BGCS.Frontend.Clang.Tests;

public sealed class ProjectSourceIdentityTests : FrontendTestHost
{
    [Theory]
    [InlineData(SourceLanguage.C, false)]
    [InlineData(SourceLanguage.C, true)]
    [InlineData(SourceLanguage.Cpp, false)]
    [InlineData(SourceLanguage.Cpp, true)]
    public async Task SiblingHeadersKeepPortableIdentitiesWithoutMergingSameBasenameFiles(SourceLanguage language, bool presumed)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-sibling-identities-" + Guid.NewGuid().ToString("N"));
        string first = Path.Combine(root, "first checkout"), second = Path.Combine(root, "longer relocated checkout");
        string literal = "(unnamed at " + Path.Combine(first, "headers", "native.h").Replace('\\', '/') + ":8:9)";
        try
        {
            foreach (string checkout in new[] { first, second })
            {
                Directory.CreateDirectory(Path.Combine(checkout, "configuration"));
                foreach (string name in new[] { "Left", "Right" })
                {
                    string directory = Path.Combine(checkout, "headers", name);
                    Directory.CreateDirectory(directory);
                    await File.WriteAllTextAsync(Path.Combine(directory, "common.h"),
                        "typedef struct " + name + " { struct { int value; } items[3]; } " + name + ";\n");
                }
                await File.WriteAllTextAsync(Path.Combine(checkout, "headers", "native.h"),
                    "#include \"Left/common.h\"\n#include \"Right/common.h\"\n#define LOOKALIKE \"" + literal + "\"\n"
                    + "#define REPLACED 0\n#undef REPLACED\n#define REPLACED 64\n#define REMOVED 1\n#undef REMOVED\nvoid Observe(Left* left, Right* right);\n");
            }
            FrontendResult original = await Read(first), relocated = await Read(second);
            Assert.Equal(JsonSerializer.Serialize(original.module), JsonSerializer.Serialize(relocated.module));
            NativeType[] arrays = original.module.types.Where(type => type.category == NativeTypeCategory.Array && type.arrayLength == 3).ToArray();
            Assert.Equal(2, arrays.Length);
            Assert.Equal(2, arrays.Select(type => type.elementType).Distinct().Count());
            Assert.Contains(original.module.declarations, declaration => declaration.source.file == "../headers/Left/common.h");
            Assert.Contains(original.module.declarations, declaration => declaration.source.file == "../headers/Right/common.h");
            Assert.All(original.module.types, type => Assert.DoesNotContain(root.Replace('\\', '/'), type.spelling, StringComparison.OrdinalIgnoreCase));
            Assert.NotEqual(JsonSerializer.Serialize(original.snapshot), JsonSerializer.Serialize(relocated.snapshot));
            Assert.Contains(original.snapshot.files, file => file.path == Path.Combine(first, "headers", "Left", "common.h"));
            Assert.Contains(relocated.snapshot.files, file => file.path == Path.Combine(second, "headers", "Right", "common.h"));
            Assert.Equal(2, original.module.declarations.Count(declaration => declaration.name == "REPLACED"));
            NativeDeclaration active = Assert.Single(original.module.declarations, declaration => declaration.name == "REPLACED" && declaration.macro?.isActive == true);
            Assert.Equal("64", active.constantValue!.value);
            Assert.False(Assert.Single(original.module.declarations, declaration => declaration.name == "REMOVED").macro!.isActive);
            NativeStringConstant constant = Assert.Single(original.module.declarations, declaration => declaration.name == "LOOKALIKE").stringConstant!;
            Assert.Equal(literal, Encoding.UTF8.GetString(constant.codeUnits.Take(constant.codeUnits.Length - 1).Select(value => checked((byte)value)).ToArray()));

            async Task<FrontendResult> Read(string checkout)
            {
                FrontendResult result = await new ClangFrontend(host).ParseAsync(new(new TargetResolver().Resolve(new(new("host"))), language,
                    [Path.Combine(checkout, "headers", "native.h")])
                { pathBase = Path.Combine(checkout, "configuration"), parseMacros = true, usePresumedSourceLocations = presumed }, CancellationToken.None);
                Assert.DoesNotContain(result.module.diagnostics, diagnostic => diagnostic.isFailure);
                return result;
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(SourceLanguage.C)]
    [InlineData(SourceLanguage.Cpp)]
    public async Task RelocatingProjectHeadersPreservesAnonymousTypesMembersAndTransitiveFacts(SourceLanguage language)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-project-identities-" + Guid.NewGuid().ToString("N"));
        string first = Path.Combine(root, "first source's directory"), second = Path.Combine(root, "another-longer-location");
        try
        {
            foreach (string directory in new[] { first, second })
            {
                Directory.CreateDirectory(Path.Combine(directory, "nested"));
                await File.WriteAllTextAsync(Path.Combine(directory, "nested", "peer.h"),
                    "struct Peer { struct { int peer_value; } payload; };\n");
                await File.WriteAllTextAsync(Path.Combine(directory, "native.h"), """
                    #include "nested/peer.h"
                    typedef struct Parent {
                        struct { int value; } child;
                        union { float x; int y; } data;
                        union { int first; float second; };
                    } Parent;
                    static int Local(int value);
                    void Observe(Parent* parent, struct Peer* peer);
                    """);
            }
            NativeModule original = await Parse(first, language), relocated = await Parse(second, language);
            Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(relocated));
            Assert.Contains(original.declarations, declaration => declaration.source.file == "nested/peer.h");
            Assert.True(original.types.Count(type => type.category == NativeTypeCategory.Record) >= 6);
            Assert.All(original.types, type => Assert.DoesNotContain(root.Replace('\\', '/'), type.spelling, StringComparison.OrdinalIgnoreCase));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(SourceLanguage.C)]
    [InlineData(SourceLanguage.Cpp)]
    public async Task ProjectProvenanceMappingPreservesLiteralBytesAndLeavesContextDependentValuesUnpublished(SourceLanguage language)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-project-literal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "native.h");
            string physical = header.Replace('\\', '/');
            string literal = "(unnamed at " + physical + ":7:9)";
            await File.WriteAllTextAsync(header, "#define CONTEXT_FILE __FILE__\nstatic const char PHYSICAL_FILE[] = __FILE__;\n#define LOOKALIKE \"" + literal + "\"\nstruct Container { struct { int value; } child; };\n");
            NativeModule module = await Parse(root, language, parseMacros: true);
            Assert.Equal(literal, Text("LOOKALIKE"));
            NativeDeclaration context = Assert.Single(module.declarations, declaration => declaration.name == "CONTEXT_FILE");
            Assert.Equal("__FILE__", context.value);
            Assert.Null(context.stringConstant);
            Assert.Null(Assert.Single(module.declarations, declaration => declaration.name == "PHYSICAL_FILE").stringConstant);
            Assert.Contains(module.types, type => type.spelling.Contains("unnamed", StringComparison.Ordinal)
                && type.spelling.Contains(" at native.h:", StringComparison.Ordinal));

            string Text(string name)
            {
                NativeStringConstant constant = Assert.Single(module.declarations, declaration => declaration.name == name).stringConstant
                    ?? throw new InvalidOperationException("Expected exact native string fact: " + name);
                return Encoding.UTF8.GetString(constant.codeUnits.Take(constant.codeUnits.Length - 1).Select(value => checked((byte)value)).ToArray());
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ATemplateStringValueThatResemblesCompilerProvenanceKeepsItsPhysicalBytes(bool generated)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-template-provenance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "native.h");
            string payload = "(unnamed at " + header.Replace('\\', '/') + ":8:9)";
            await File.WriteAllTextAsync(header, """
                template<unsigned N> struct FixedText {
                    char data[N];
                    constexpr FixedText(const char (&text)[N]) { for (unsigned i=0; i<N; ++i) data[i]=text[i]; }
                };
                template<FixedText Text> struct Tagged { int value; };
                """ + "\nusing LiteralTagged = Tagged<\"" + payload + "\">;\nLiteralTagged* get_tagged();\n");
            FrontendRequest request = new(new TargetResolver().Resolve(new(new("host"))), SourceLanguage.Cpp, [header])
            {
                pathBase = root, parseMacros = false,
                sourcePathMappings = generated ? ImmutableDictionary<string, string>.Empty.Add(header, "contributions/native.h")
                    : ImmutableDictionary<string, string>.Empty
            };
            NativeModule module = (await new ClangFrontend(host).ParseAsync(request, CancellationToken.None)).module;
            Assert.DoesNotContain(module.diagnostics, diagnostic => diagnostic.isFailure);
            NativeType[] tagged = module.types.Where(type => type.spelling.Contains("Tagged<", StringComparison.Ordinal)).ToArray();
            Assert.NotEmpty(tagged);
            Assert.Contains(tagged, type => type.spelling.Contains(payload, StringComparison.Ordinal));
            Assert.DoesNotContain(tagged, type => type.spelling.Contains("(unnamed at " + (generated ? "contributions/" : "") + "native.h:8:9)", StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private async Task<NativeModule> Parse(string root, SourceLanguage language, bool parseMacros = false)
    {
        FrontendResult result = await new ClangFrontend(host).ParseAsync(new(new TargetResolver().Resolve(new(new("host"))), language,
            [Path.Combine(root, "native.h")]) { pathBase = root, parseMacros = parseMacros }, CancellationToken.None);
        Assert.DoesNotContain(result.module.diagnostics, diagnostic => diagnostic.isFailure);
        return result.module;
    }
}
