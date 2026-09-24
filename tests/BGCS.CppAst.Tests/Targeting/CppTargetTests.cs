using System;
using System.Linq;
using System.Runtime.InteropServices;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Metadata;
using BGCS.CppAst.Model.Types;
using BGCS.CppAst.Parsing;
using BGCS.CppAst.Targeting;
using Xunit;

namespace BGCS.CppAst.Tests;

public sealed class CppTargetTests
{
    [Theory]
    [InlineData(CppTargetPlatform.Windows, CppTargetArchitecture.X64, CppTargetAbi.Msvc, "x86_64-pc-windows-msvc", "windows-x64-msvc")]
    [InlineData(CppTargetPlatform.Linux, CppTargetArchitecture.X64, CppTargetAbi.Gnu, "x86_64-unknown-linux-gnu", "linux-x64-gnu")]
    [InlineData(CppTargetPlatform.Linux, CppTargetArchitecture.Arm64, CppTargetAbi.Musl, "aarch64-unknown-linux-musl", "linux-arm64-musl")]
    [InlineData(CppTargetPlatform.MacOS, CppTargetArchitecture.Arm64, CppTargetAbi.Darwin, "arm64-apple-darwin", "macos-arm64-darwin")]
    public void Resolve_ExplicitTarget_ShouldProduceStableTripleAndIdentifier(
        CppTargetPlatform platform,
        CppTargetArchitecture architecture,
        CppTargetAbi abi,
        string triple,
        string identifier)
    {
        CppTarget target = CppTarget.Resolve(platform, architecture, abi);

        Assert.Equal(triple, target.Triple);
        Assert.Equal(identifier, target.Identifier);
    }

    [Fact]
    public void Resolve_InvalidAbi_ShouldFailClearly()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            CppTarget.Resolve(CppTargetPlatform.MacOS, CppTargetArchitecture.Arm64, CppTargetAbi.Msvc));

        Assert.Contains("not valid", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigureForTarget_ReplacesArchitectureMacrosWithoutTouchingUserDefines()
    {
        CppParserOptions options = new();
        options.Defines.Add("USER_FEATURE=1");
        CppTarget x64 = CppTarget.Resolve(CppTargetPlatform.Windows, CppTargetArchitecture.X64, CppTargetAbi.Msvc);
        CppTarget x86 = CppTarget.Resolve(CppTargetPlatform.Windows, CppTargetArchitecture.X86, CppTargetAbi.Msvc);

        options.ConfigureForTarget(x64, discoverHostToolchain: false);
        options.ConfigureForTarget(x86, discoverHostToolchain: false);

        Assert.Contains("USER_FEATURE=1", options.Defines);
        Assert.Contains("_M_IX86=600", options.Defines);
        Assert.DoesNotContain("_WIN64=1", options.Defines);
        Assert.DoesNotContain("_M_X64=100", options.Defines);
        Assert.Equal(1, options.Defines.Count(value => value == "_WIN32=1"));
        Assert.Equal(1, options.AdditionalArguments.Count(value => value == "-fms-extensions"));
    }

    [Fact]
    public void ConfigureForTarget_DropsPreviousSysrootWhenTargetChanges()
    {
        CppParserOptions options = new();
        string sysroot = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
        options.ConfigureForTarget(
            CppTarget.Resolve(CppTargetPlatform.MacOS, CppTargetArchitecture.X64, CppTargetAbi.Darwin),
            sysRoot: sysroot, discoverHostToolchain: false);
        Assert.Contains("-isysroot", options.AdditionalArguments);
        Assert.Contains(sysroot, options.AdditionalArguments);

        options.ConfigureForTarget(
            CppTarget.Resolve(CppTargetPlatform.Linux, CppTargetArchitecture.X64, CppTargetAbi.Gnu),
            discoverHostToolchain: false);

        Assert.DoesNotContain("-isysroot", options.AdditionalArguments);
        Assert.DoesNotContain(sysroot, options.AdditionalArguments);
        Assert.Equal("x86_64-unknown-linux-gnu", options.TargetTriple);
    }

    [Fact]
    public void ConfigureForTarget_HostCpp_ShouldParseStandardLibrary()
    {
        CppTarget target = CppTarget.Resolve();
        CppParserOptions options = new()
        {
            ParserKind = CppParserKind.Cpp,
            ParseMacros = false,
            ParseComments = false,
            ParseSystemIncludes = false
        };
        options.ConfigureForTarget(target);

        var compilation = CppParser.Parse("#include <vector>\nstruct NativeVectorHolder { std::vector<int> values; };", options);

        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics.Messages));
        Assert.Equal(target.Triple, options.TargetTriple);
        Assert.NotEmpty(options.SystemIncludeFolders);
    }

    [Fact]
    public void Resolve_Host_ShouldMatchProcessArchitecture()
    {
        CppTarget target = CppTarget.Resolve();
        CppTargetArchitecture expected = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X86 => CppTargetArchitecture.X86,
            Architecture.X64 => CppTargetArchitecture.X64,
            Architecture.Arm => CppTargetArchitecture.Arm,
            Architecture.Arm64 => CppTargetArchitecture.Arm64,
            _ => throw new PlatformNotSupportedException()
        };

        Assert.Equal(expected, target.Architecture);
        Assert.DoesNotContain("host", target.Identifier, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CompilerFingerprint_ShouldBeStableAndIncludeResolvedDriverIdentity()
    {
        string compiler = CppToolchainDiscovery.FindCompiler(CppParserKind.Cpp)
            ?? throw new InvalidOperationException("A host C++ compiler is required for toolchain fingerprinting.");

        string first = CppToolchainDiscovery.GetCompilerFingerprint(CppParserKind.Cpp, compiler);
        string second = CppToolchainDiscovery.GetCompilerFingerprint(CppParserKind.Cpp, compiler);

        Assert.Equal(first, second);
        Assert.Contains(compiler.Replace('\\', '/'), first, StringComparison.Ordinal);
        Assert.Contains("length:", first, StringComparison.Ordinal);
        Assert.DoesNotContain("version:unavailable", first, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CppTargetPlatform.Windows, CppTargetArchitecture.X64, CppTargetAbi.Msvc, 4, 2, 8)]
    [InlineData(CppTargetPlatform.Linux, CppTargetArchitecture.X64, CppTargetAbi.Gnu, 8, 4, 16)]
    [InlineData(CppTargetPlatform.Linux, CppTargetArchitecture.Arm64, CppTargetAbi.Gnu, 8, 4, 16)]
    [InlineData(CppTargetPlatform.MacOS, CppTargetArchitecture.Arm64, CppTargetAbi.Darwin, 8, 4, 8)]
    public void Parse_Primitives_ShouldUseTargetAbiSizes(
        CppTargetPlatform platform,
        CppTargetArchitecture architecture,
        CppTargetAbi abi,
        int longSize,
        int wcharSize,
        int longDoubleSize)
    {
        CppParserOptions options = new()
        {
            ParserKind = CppParserKind.Cpp,
            ParseMacros = false,
            ParseComments = false,
            ParseSystemIncludes = false
        };
        options.ConfigureForTarget(CppTarget.Resolve(platform, architecture, abi), discoverHostToolchain: false);

        CppCompilation compilation = CppParser.Parse(
            "struct NativeAbiValues { char plainChar; long signedLong; unsigned long unsignedLong; wchar_t wide; long double extendedValue; };",
            options);

        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics.Messages));
        CppClass type = Assert.Single(compilation.Classes, value => value.Name == "NativeAbiValues");
        Assert.Equal(CppPrimitiveKind.Char,
            Assert.IsType<CppPrimitiveType>(type.Fields.Single(field => field.Name == "plainChar").Type).Kind);
        Assert.Equal(longSize, Assert.IsType<CppPrimitiveType>(type.Fields.Single(field => field.Name == "signedLong").Type).SizeOf);
        Assert.Equal(longSize, Assert.IsType<CppPrimitiveType>(type.Fields.Single(field => field.Name == "unsignedLong").Type).SizeOf);
        Assert.Equal(wcharSize, Assert.IsType<CppPrimitiveType>(type.Fields.Single(field => field.Name == "wide").Type).SizeOf);
        Assert.Equal(longDoubleSize, Assert.IsType<CppPrimitiveType>(type.Fields.Single(field => field.Name == "extendedValue").Type).SizeOf);
    }
}
