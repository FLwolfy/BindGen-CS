using System;
using System.Linq;
using System.Threading.Tasks;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Core.Targets;
using BGCS.Extension.BrowserWasm;
using BGCS.Targets.Desktop;
using Xunit;

namespace BGCS.Frontend.Clang.Tests;

public sealed class ClangResourceHeaderTests : FrontendTestHost
{
    [Theory]
    [InlineData("windows-x64-msvc", 24)]
    [InlineData("linux-arm64-gnu", 24)]
    [InlineData("emscripten-wasm32-emscripten", 12)]
    public async Task BuiltinHeadersRemainAvailableWithoutAHostCompilerOrSdk(string id, int expectedSize)
    {
        INativeTargetProvider provider = id.StartsWith("emscripten", StringComparison.Ordinal) ? new BrowserWasmTargetProvider() : new DesktopTargetProvider();
        // Declaring an unavailable driver explicitly prevents any native SDK discovery; builtin headers remain bundled.
        Assert.True(provider.TryResolve(new(new(id), new(compilerPath: "uninstalled-parser-location")), out NativeTargetDescriptor? target));
        Assert.Empty(target!.toolchain.systemIncludeFolders);
        NativeModule module = (await ParseSourceAsync("#include <stddef.h>\nstruct HeaderLayout { size_t size; ptrdiff_t offset; void* handle; };", target, SourceLanguage.C)).module;
        Assert.DoesNotContain(module.diagnostics, diagnostic => diagnostic.isFailure);
        NativeDeclaration record = Assert.Single(module.declarations, value => value.name == "HeaderLayout");
        Assert.Equal(expectedSize, module.GetType(record.type!.Value).size);
    }
    [Fact]
    public void HostSdkDiscoveryDoesNotImportAnotherClangReleasesBuiltinHeaders()
    {
        Assert.True(new DesktopTargetProvider().TryResolve(new(new("host")) { language = SourceLanguage.Cpp }, out NativeTargetDescriptor? target));
        Assert.NotEmpty(target!.headerSearchPaths);
        Assert.DoesNotContain(target.headerSearchPaths, entry => string.Equals(new System.IO.DirectoryInfo(entry.path).Parent?.Parent?.Name, "clang", StringComparison.OrdinalIgnoreCase));
    }
}
