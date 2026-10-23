using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.Frontend.Clang;
using BGCS.Targets.Desktop.Targeting;
using Xunit;

namespace BGCS.Frontend.Clang.Tests;

public sealed class ReferencedSystemAliasTests : FrontendTestHost
{
    [Theory]
    [InlineData(SourceLanguage.C)]
    [InlineData(SourceLanguage.Cpp)]
    public async Task ReferencedSdkTypedefs_RetainAliasChainWithoutIncludingUnrelatedSystemDeclarations(SourceLanguage language)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-referenced-sdk-alias-" + Guid.NewGuid().ToString("N"));
        string sdk = Path.Combine(root, "sdk");
        Directory.CreateDirectory(sdk);
        try
        {
            string header = Path.Combine(root, "entry.h");
            await File.WriteAllTextAsync(Path.Combine(sdk, "sdk_aliases.h"), "typedef long long SignedWord; typedef SignedWord NestedWord; typedef unsigned long long UnreferencedWord; int unreferenced_system_function(void);");
            await File.WriteAllTextAsync(header, "#include <sdk_aliases.h>\nNestedWord transform_word(NestedWord value);");
            FrontendRequest request = new(new TargetResolver().Resolve(new(new("host"))), language, [header])
            { systemIncludeDirectories = [sdk], pathBase = root, parseSystemIncludes = false, parseMacros = false };
            FrontendResult result = await new ClangFrontend(host).ParseAsync(request, CancellationToken.None);
            Assert.NotNull(result.executionInputs);
            Assert.Contains(result.executionInputs.files, file => !result.snapshot.files.Any(parsed => parsed.path == file.path));
            Assert.Contains(result.executionInputs.searchDirectories, directory =>
                result.executionInputs.files.Any(file => file.path.StartsWith(directory.path + Path.DirectorySeparatorChar, StringComparison.Ordinal)));
            NativeModule module = result.module;
            Assert.DoesNotContain(module.diagnostics, diagnostic => diagnostic.isFailure);
            NativeDeclaration callable = Assert.Single(module.declarations, declaration => declaration.name == "transform_word");
            NativeType outer = module.GetType(callable.returnType!.Value);
            Assert.Equal(NativeTypeCategory.Alias, outer.category);
            Assert.Equal("NestedWord", outer.spelling);
            Assert.True(module.GetDeclaration(outer.declaration!.Value).isSystemDeclaration);
            NativeType inner = module.GetType(outer.elementType!.Value);
            Assert.Equal(NativeTypeCategory.Alias, inner.category);
            Assert.Equal("SignedWord", inner.spelling);
            NativeType integer = module.GetType(inner.elementType!.Value);
            Assert.Equal(NativeTypeCategory.Primitive, integer.category);
            Assert.Equal(8, integer.size);
            Assert.True(integer.isSigned);
            NativeDeclaration parameter = module.GetDeclaration(Assert.Single(callable.children));
            Assert.Equal(outer.id, parameter.type);
            Assert.DoesNotContain(module.declarations, declaration => declaration.name is "UnreferencedWord" or "unreferenced_system_function");
            Assert.DoesNotContain(module.types, type => type.spelling == "UnreferencedWord");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
