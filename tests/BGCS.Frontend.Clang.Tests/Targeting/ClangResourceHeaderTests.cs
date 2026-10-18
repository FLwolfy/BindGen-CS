using BGCS.Contracts.Inputs;
using System;
using BGCS.Contracts.Targets;
using BGCS.Frontend.Clang.Model.Declarations;
using BGCS.Frontend.Clang.Parsing;
using BGCS.Platforms.Targeting;
using BGCS.Platforms.Toolchains;
using System.Linq;
using Xunit;

namespace BGCS.Frontend.Clang.Tests;

public sealed class ClangResourceHeaderTests
{
    [Theory]
    [InlineData("windows", "x64", "msvc", 24)]
    [InlineData("linux", "arm64", "gnu", 24)]
    [InlineData("emscripten", "wasm32", "emscripten", 12)]
    public void BuiltinHeadersRemainAvailableWithoutAHostCompilerOrSdk(
        string platform,
        string architecture,
        string abi,
        int expectedSize
    ) {
        var options = new CppParserOptions
        {
            parserKind = SourceLanguage.C,
            parseSystemIncludes = false,
            parseMacros = false
        };
        options.ConfigureForTarget(new TargetResolver().Resolve(new(new NativeTargetId(string.Join("-", platform, architecture, abi)))));

        var compilation = CppParser.Parse(
            "#include <stddef.h>\nstruct HeaderLayout { size_t size; ptrdiff_t offset; void* handle; };", options);

        Assert.False(compilation.hasErrors, string.Join(Environment.NewLine, compilation.diagnostics.messages));
        CppClass declaration = Assert.Single(compilation.classes, type => type.name == "HeaderLayout");
        Assert.Equal(expectedSize, declaration.sizeOf);
        Assert.Empty(options.systemIncludeFolders);
    }

    [Fact]
    public void HostSdkDiscoveryDoesNotImportAnotherClangReleasesBuiltinHeaders()
    {
        NativeTargetDescriptor target = ToolchainResolver.ResolveToolchain(new TargetResolver().Resolve(new(new("host"))), SourceLanguage.Cpp);
        var directories = ToolchainResolver.ResolveHeaderSearchPaths(target, SourceLanguage.Cpp).Select(path => path.path).ToArray();

        Assert.NotEmpty(directories);
        Assert.DoesNotContain(directories, directory =>
            string.Equals(new System.IO.DirectoryInfo(directory).Parent?.Parent?.Name, "clang", StringComparison.OrdinalIgnoreCase));
    }
}
