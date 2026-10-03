using System;
using System.Linq;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Parsing;
using BGCS.CppAst.Targeting;
using Xunit;

namespace BGCS.CppAst.Tests;

public sealed class ClangResourceHeaderTests
{
    [Theory]
    [InlineData(CppTargetPlatform.Windows, CppTargetArchitecture.X64, CppTargetAbi.Msvc, 24)]
    [InlineData(CppTargetPlatform.Linux, CppTargetArchitecture.Arm64, CppTargetAbi.Gnu, 24)]
    [InlineData(CppTargetPlatform.Emscripten, CppTargetArchitecture.Wasm32, CppTargetAbi.Emscripten, 12)]
    public void BuiltinHeadersRemainAvailableWithoutAHostCompilerOrSdk(
        CppTargetPlatform platform,
        CppTargetArchitecture architecture,
        CppTargetAbi abi,
        int expectedSize
    ) {
        var options = new CppParserOptions
        {
            ParserKind = CppParserKind.C,
            ParseSystemIncludes = false,
            ParseMacros = false
        };
        options.ConfigureForTarget(CppTarget.Resolve(platform, architecture, abi), discoverHostToolchain: false);

        var compilation = CppParser.Parse(
            "#include <stddef.h>\nstruct HeaderLayout { size_t size; ptrdiff_t offset; void* handle; };", options);

        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics.Messages));
        CppClass declaration = Assert.Single(compilation.Classes, type => type.Name == "HeaderLayout");
        Assert.Equal(expectedSize, declaration.SizeOf);
        Assert.Empty(options.SystemIncludeFolders);
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
