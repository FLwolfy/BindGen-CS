using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.Contracts.Targets;
using Xunit;

namespace BGCS.Frontend.Clang.Tests;

public sealed class NativeContextStringMacroTests
{
    [Theory]
    [InlineData(SourceLanguage.C)]
    [InlineData(SourceLanguage.Cpp)]
    public async Task ContextDependentTextAndBuiltinsNeverBecomeProbeSitePublicConstants(SourceLanguage language)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-context-text-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "contexts.h");
            await File.WriteAllTextAsync(header, """
                #define CONTEXT_FILE __FILE_NAME__
                #define CONTEXT_FUNCTION __FUNCTION__
                #define CONTEXT_FUNC __func__
                #define CONTEXT_PRETTY __PRETTY_FUNCTION__
                #define CONTEXT_SIGNATURE __FUNCSIG__
                #define CONTEXT_DECORATED __FUNCDNAME__
                #define CONTEXT_BUILTIN_FUNCTION __builtin_FUNCTION()
                #define CONTEXT_BUILTIN_FILE __builtin_FILE()
                #define CONTEXT_BUILTIN_LINE __builtin_LINE()
                #define CONTEXT_BUILTIN_COLUMN __builtin_COLUMN()
                #define CONTEXT_CHAIN CONTEXT_BUILTIN_FUNCTION
                #define CONTEXT_CAPABILITY __has_builtin(__builtin_add_overflow)
                #define CONTEXT_CAPABILITY_CHAIN CONTEXT_CAPABILITY
                #define CONTEXT_FEATURE __has_feature(cxx_exceptions)
                #define CONTEXT_EXTENSION __has_extension(cxx_rvalue_references)
                #define CONTEXT_IDENTIFIER __is_identifier(__unknown_identifier)
                #define CONTEXT_ATTRIBUTE __has_attribute(aligned)
                #define CONTEXT_TARGET __is_target_arch(x86_64)
                #define STABLE_TEXT "stable"
                """);
            NativeTargetDescriptor target = new(new("context-text"), "test", "test", "test", "x86_64-pc-windows-msvc", new());
            NativeModule module = (await new ClangFrontend().ParseAsync(new(target, language, [header]) { pathBase = root }, CancellationToken.None)).module;
            Assert.DoesNotContain(module.diagnostics, diagnostic => diagnostic.isFailure);
            NativeDeclaration[] contexts = module.declarations.Where(declaration => declaration.kind == "Macro" && declaration.name.StartsWith("CONTEXT_", StringComparison.Ordinal)).ToArray();
            Assert.Equal(18, contexts.Length);
            Assert.All(contexts, declaration =>
            {
                Assert.Null(declaration.constantValue);
                Assert.Null(declaration.stringConstant);
                Assert.Null(declaration.macro!.functionAlias);
                Assert.True(declaration.macro.isActive);
            });
            Assert.Equal(new uint[] { 115, 116, 97, 98, 108, 101, 0 },
                Assert.Single(module.declarations, declaration => declaration.name == "STABLE_TEXT").stringConstant!.codeUnits);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
