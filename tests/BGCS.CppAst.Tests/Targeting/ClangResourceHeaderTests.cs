using System;
using BGCS.Core.Targeting;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Parsing;
using BGCS.CppAst.Targeting;
using Xunit;

namespace BGCS.CppAst.Tests;

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
            parserKind = CppParserKind.C,
            parseSystemIncludes = false,
            parseMacros = false
        };
        options.ConfigureForTarget(new ClangTargetResolver().Resolve(new(new NativeTargetId(string.Join("-", platform, architecture, abi)))), discoverHostToolchain: false);

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
        var directories = CppToolchainDiscovery.DiscoverSystemIncludeFolders(CppParserKind.Cpp);

        Assert.NotEmpty(directories);
        Assert.DoesNotContain(directories, directory =>
            string.Equals(new System.IO.DirectoryInfo(directory).Parent?.Parent?.Name, "clang", StringComparison.OrdinalIgnoreCase));
    }
}
