using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Targets.Desktop.Targeting;
using Xunit;

namespace BGCS.Frontend.Clang.Tests;

public sealed class NativeHeaderHelperFactsTests : FrontendTestHost
{
    [Fact]
    public async Task CHeaderDefinitionsRetainInlineInternalAndExternalLinkageFacts()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-c-header-facts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "helpers.h");
            await File.WriteAllTextAsync(header, """
                static inline int Local(int value) { return value + 1; }
                inline int InlineExternal(int value) { return value + 2; }
                int External(int value);
                static int Forward(int value);
                static int Forward(int value) { return value + 3; }
                """);
            NativeModule module = (await new ClangFrontend(host).ParseAsync(new(new TargetResolver().Resolve(new(new("host"))), SourceLanguage.C, [header])
            { pathBase = root, parseMacros = false }, CancellationToken.None)).module;
            Assert.DoesNotContain(module.diagnostics, diagnostic => diagnostic.isFailure);
            NativeDeclaration local = Function("Local");
            Assert.True(local.isInline); Assert.True(local.isDefinition); Assert.True(local.isStatic);
            Assert.Equal(NativeLinkageKind.Internal, local.nativeLinkage);
            NativeDeclaration inline = Function("InlineExternal");
            Assert.True(inline.isInline); Assert.True(inline.isDefinition); Assert.False(inline.isStatic);
            Assert.Equal(NativeLinkageKind.External, inline.nativeLinkage);
            NativeDeclaration external = Function("External");
            Assert.False(external.isInline); Assert.False(external.isDefinition);
            Assert.Equal(NativeLinkageKind.External, external.nativeLinkage);
            Assert.True(Function("Forward").isDefinition);
            NativeDeclaration Function(string name) => Assert.Single(module.declarations, declaration => declaration.kind == "Function" && declaration.name == name);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
