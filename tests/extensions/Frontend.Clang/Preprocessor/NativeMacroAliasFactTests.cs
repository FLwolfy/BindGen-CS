using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Core.Targets;
using Xunit;

namespace BGCS.Frontend.Clang.Tests;

public sealed class NativeMacroAliasFactTests : FrontendTestHost
{
    [Theory]
    [InlineData(SourceLanguage.C)]
    [InlineData(SourceLanguage.Cpp)]
    public async Task ObjectAliasesResolveActualCompilerFunctionReferencesInsteadOfMatchingReplacementText(SourceLanguage language)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-macro-alias-facts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "aliases.h");
            await File.WriteAllTextAsync(header, """
                int Add(int left, int right);
                int Other(int left, int right);
                int (*Pointer)(int, int);
                #define PublicAdd Add
                #define ParenthesizedAdd (Add)
                #define ChainedAdd PublicAdd
                #define NotAFunction Pointer
                #define NotAnAlias Add(1, 2)
                #define FunctionLike(left, right) Add(left, right)
                #define Changed Add
                #undef Changed
                #define Changed Other
                """);
            NativeTargetDescriptor target = new(new("macro-alias-test"), "test", "test", "test", "x86_64-pc-windows-msvc", new());
            NativeModule module = (await new ClangFrontend(host).ParseAsync(new(target, language, [header]) { pathBase = root }, CancellationToken.None)).module;
            Assert.DoesNotContain(module.diagnostics, diagnostic => diagnostic.isFailure);
            NativeDeclaration add = Assert.Single(module.declarations, declaration => declaration.kind == "Function" && declaration.name == "Add");
            foreach (string name in new[] { "PublicAdd", "ParenthesizedAdd", "ChainedAdd" })
            {
                NativeDeclaration alias = Assert.Single(module.declarations, declaration => declaration.kind == "Macro" && declaration.name == name);
                Assert.Equal(add.id, alias.macro!.functionAlias);
                Assert.Equal(EvidenceLevel.Declared, alias.macro.functionAliasEvidence!.level);
                Assert.Null(alias.constantValue);
            }
            foreach (string name in new[] { "NotAFunction", "NotAnAlias", "FunctionLike" })
                Assert.Null(Assert.Single(module.declarations, declaration => declaration.kind == "Macro" && declaration.name == name).macro!.functionAlias);
            NativeDeclaration changed = Assert.Single(module.declarations, declaration => declaration.kind == "Macro" && declaration.name == "Changed" && declaration.macro!.isActive == true);
            Assert.Equal(Assert.Single(module.declarations, declaration => declaration.kind == "Function" && declaration.name == "Other").id, changed.macro!.functionAlias);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
