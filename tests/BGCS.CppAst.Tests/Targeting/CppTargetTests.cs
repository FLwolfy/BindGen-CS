using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using BGCS.Core.Targeting;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Metadata;
using BGCS.CppAst.Model.Types;
using BGCS.CppAst.Parsing;
using BGCS.CppAst.Targeting;
using Xunit;

namespace BGCS.CppAst.Tests;

public sealed class CppTargetTests
{
    [Fact]
    public void CppSdkWrappersPrecedeCHeadersAndIncludeNextReachesTheCAbi()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-include-order-" + Guid.NewGuid().ToString("N"));
        string c = Path.Combine(root, "c");
        string cxx = Path.Combine(root, "cxx");
        Directory.CreateDirectory(c);
        Directory.CreateDirectory(cxx);
        File.WriteAllText(Path.Combine(c, "stdint.h"), "#ifndef __CLANG_STDINT_H\n#error Parser builtin headers were bypassed\n#endif\n#define BGCS_C_HEADER 1\ntypedef unsigned int bgcs_uint;\n");
        File.WriteAllText(Path.Combine(cxx, "stdint.h"), "#define BGCS_CXX_HEADER 1\n#include_next <stdint.h>\n");
        try
        {
            NativeToolchainDescriptor toolchain = new(systemIncludeFolders: [c], cxxSystemIncludeFolders: [cxx]);
            NativeTargetDescriptor target = new ClangTargetResolver().Resolve(new(new NativeTargetId("linux-x64-gnu"), toolchain));
            CppParserOptions options = new() { parserKind = CppParserKind.Cpp, parseSystemIncludes = false };
            options.ConfigureForTarget(target, discoverHostToolchain: false);
            CppCompilation compilation = CppParser.Parse("""
                #include <stdint.h>
                #ifndef BGCS_CXX_HEADER
                #error C++ SDK wrappers were bypassed
                #endif
                #ifndef BGCS_C_HEADER
                #error include_next did not reach the C ABI
                #endif
                struct AbiValue { bgcs_uint value; };
                """, options);
            Assert.False(compilation.hasErrors, string.Join(Environment.NewLine, compilation.diagnostics.messages));
            Assert.Equal(4, Assert.Single(compilation.classes, value => value.name == "AbiValue").sizeOf);
            options.parserKind = CppParserKind.C;
            options.ConfigureForTarget(target, discoverHostToolchain: false);
            Assert.DoesNotContain(cxx, options.systemIncludeFolders);
            Assert.Contains(c, options.systemIncludeFolders);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("windows", "x64", "msvc", "x86_64-pc-windows-msvc", "windows-x64-msvc")]
    [InlineData("linux", "x64", "gnu", "x86_64-unknown-linux-gnu", "linux-x64-gnu")]
    [InlineData("linux", "arm64", "musl", "aarch64-unknown-linux-musl", "linux-arm64-musl")]
    [InlineData("macos", "arm64", "darwin", "arm64-apple-darwin", "macos-arm64-darwin")]
    [InlineData("emscripten", "wasm32", "emscripten", "wasm32-unknown-emscripten", "emscripten-wasm32-emscripten")]
    public void Resolve_ExplicitTarget_ShouldProduceStableTripleAndIdentifier(
        string platform,
        string architecture,
        string abi,
        string triple,
        string identifier)
    {
        NativeTargetDescriptor target = new ClangTargetResolver().Resolve(new(new NativeTargetId(string.Join("-", platform, architecture, abi))));

        Assert.Equal(triple, target.triple);
        Assert.Equal(identifier, target.targetId.value);
    }

    [Fact]
    public void Resolve_InvalidAbi_ShouldFailClearly()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new ClangTargetResolver().Resolve(new(new NativeTargetId("macos-arm64-msvc"))));

        Assert.Contains("not valid", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_EmscriptenStructure_ShouldUseWasm32PointerLayout()
    {
        CppParserOptions options = new()
        {
            parserKind = CppParserKind.C,
            parseMacros = false,
            parseComments = false,
            parseSystemIncludes = false
        };
        options.ConfigureForTarget(
            new ClangTargetResolver().Resolve(new(new NativeTargetId("emscripten-wasm32-emscripten"))),
            discoverHostToolchain: false);

        CppCompilation compilation = CppParser.Parse(
            "struct BrowserAbi { void* handle; unsigned long count; };",
            options);

        Assert.False(compilation.hasErrors, string.Join(Environment.NewLine, compilation.diagnostics.messages));
        CppClass type = Assert.Single(compilation.classes, value => value.name == "BrowserAbi");
        Assert.Equal(8, type.sizeOf);
        Assert.Equal(4, type.fields.Single(field => field.name == "handle").type.sizeOf);
        Assert.Equal(4, type.fields.Single(field => field.name == "count").type.sizeOf);
    }

    [Fact]
    public void ConfigureForTarget_ReplacesArchitectureMacrosWithoutTouchingUserDefines()
    {
        CppParserOptions options = new();
        options.defines.Add("USER_FEATURE=1");
        NativeTargetDescriptor x64 = new ClangTargetResolver().Resolve(new(new NativeTargetId("windows-x64-msvc")));
        NativeTargetDescriptor x86 = new ClangTargetResolver().Resolve(new(new NativeTargetId("windows-x86-msvc")));

        options.ConfigureForTarget(x64, discoverHostToolchain: false);
        options.ConfigureForTarget(x86, discoverHostToolchain: false);

        Assert.Contains("USER_FEATURE=1", options.defines);
        Assert.Contains("_M_IX86=600", options.defines);
        Assert.DoesNotContain("_WIN64=1", options.defines);
        Assert.DoesNotContain("_M_X64=100", options.defines);
        Assert.Equal(1, options.defines.Count(value => value == "_WIN32=1"));
        Assert.Equal(1, options.additionalArguments.Count(value => value == "-fms-extensions"));
    }

    [Fact]
    public void ConfigureForTarget_DropsPreviousSysrootWhenTargetChanges()
    {
        CppParserOptions options = new();
        string sysroot = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
        options.ConfigureForTarget(
            new ClangTargetResolver().Resolve(new(new NativeTargetId("macos-x64-darwin"), new(sysRoot: sysroot))),
            discoverHostToolchain: false);
        Assert.Contains("--sysroot=" + sysroot, options.additionalArguments);
        Assert.Contains("-isysroot", options.additionalArguments);
        Assert.Contains(sysroot, options.additionalArguments);

        options.ConfigureForTarget(
            new ClangTargetResolver().Resolve(new(new NativeTargetId("linux-x64-gnu"))),
            discoverHostToolchain: false);

        Assert.DoesNotContain(options.additionalArguments, argument => argument.StartsWith("--sysroot=", StringComparison.Ordinal));
        Assert.DoesNotContain("-isysroot", options.additionalArguments);
        Assert.DoesNotContain(sysroot, options.additionalArguments);
        Assert.Equal("x86_64-unknown-linux-gnu", options.targetTriple);
    }

    [Fact]
    public void ConfigureForTarget_ExplicitCppHeadersReplaceImplicitIncludesAndAreClearedOnRetarget()
    {
        CppParserOptions options = new() { parserKind = CppParserKind.Cpp };
        NativeToolchainDescriptor toolchain = new(cxxSystemIncludeFolders: [Path.GetTempPath()]);
        options.ConfigureForTarget(
            new ClangTargetResolver().Resolve(new(new NativeTargetId("linux-x64-gnu"), toolchain)),
            discoverHostToolchain: false);

        Assert.Contains("-nostdinc++", options.additionalArguments);
        Assert.Contains(Path.GetTempPath(), options.systemIncludeFolders);

        options.ConfigureForTarget(
            new ClangTargetResolver().Resolve(new(new NativeTargetId("emscripten-wasm32-emscripten"))),
            discoverHostToolchain: false);

        Assert.DoesNotContain("-nostdinc++", options.additionalArguments);
        Assert.DoesNotContain(Path.GetTempPath(), options.systemIncludeFolders);
    }

    [Theory]
    [InlineData("c++17")]
    [InlineData("c++23")]
    public void ConfigureForTarget_HostCpp_ShouldParseStandardLibrary(string standard)
    {
        NativeTargetDescriptor target = new ClangTargetResolver().Resolve(new(new NativeTargetId("host")));
        CppParserOptions options = new()
        {
            parserKind = CppParserKind.Cpp,
            parseMacros = false,
            parseComments = false,
            parseSystemIncludes = false
        };
        options.ConfigureForTarget(target);
        options.additionalArguments.Add("-std=" + standard);

        using var compilation = CppParser.Parse(
            "#include <cstddef>\n#include <cstdio>\n#include <string>\n#include <vector>\n"
            + "struct NativeVectorHolder { std::vector<std::string> values; std::size_t size; FILE* file; };", options);

        Assert.False(compilation.hasErrors, string.Join(Environment.NewLine, compilation.diagnostics.messages));
        Assert.Equal(target.triple, options.targetTriple);
        Assert.NotEmpty(options.systemIncludeFolders);
    }

    [Fact]
    public void Resolve_Host_ShouldMatchProcessArchitecture()
    {
        NativeTargetDescriptor target = new ClangTargetResolver().Resolve(new(new NativeTargetId("host")));
        string expected = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X86 => "x86",
            Architecture.X64 => "x64",
            Architecture.Arm => "arm",
            Architecture.Arm64 => "arm64",
            _ => throw new PlatformNotSupportedException()
        };

        Assert.Equal(expected, target.architectureId);
        Assert.DoesNotContain("host", target.targetId.value, StringComparison.OrdinalIgnoreCase);
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
    [InlineData("windows", "x64", "msvc", 4, 2, 8)]
    [InlineData("linux", "x64", "gnu", 8, 4, 16)]
    [InlineData("linux", "arm64", "gnu", 8, 4, 16)]
    [InlineData("macos", "arm64", "darwin", 8, 4, 8)]
    [InlineData("emscripten", "wasm32", "emscripten", 4, 4, 16)]
    public void Parse_Primitives_ShouldUseTargetAbiSizes(
        string platform,
        string architecture,
        string abi,
        int longSize,
        int wcharSize,
        int longDoubleSize)
    {
        CppParserOptions options = new()
        {
            parserKind = CppParserKind.Cpp,
            parseMacros = false,
            parseComments = false,
            parseSystemIncludes = false
        };
        options.ConfigureForTarget(new ClangTargetResolver().Resolve(new(new NativeTargetId(string.Join("-", platform, architecture, abi)))), discoverHostToolchain: false);

        CppCompilation compilation = CppParser.Parse(
            "struct NativeAbiValues { char plainChar; long signedLong; unsigned long unsignedLong; wchar_t wide; long double extendedValue; };",
            options);

        Assert.False(compilation.hasErrors, string.Join(Environment.NewLine, compilation.diagnostics.messages));
        CppClass type = Assert.Single(compilation.classes, value => value.name == "NativeAbiValues");
        Assert.Equal(CppPrimitiveKind.Char,
            Assert.IsType<CppPrimitiveType>(type.fields.Single(field => field.name == "plainChar").type).kind);
        Assert.Equal(longSize, Assert.IsType<CppPrimitiveType>(type.fields.Single(field => field.name == "signedLong").type).sizeOf);
        Assert.Equal(longSize, Assert.IsType<CppPrimitiveType>(type.fields.Single(field => field.name == "unsignedLong").type).sizeOf);
        Assert.Equal(wcharSize, Assert.IsType<CppPrimitiveType>(type.fields.Single(field => field.name == "wide").type).sizeOf);
        Assert.Equal(longDoubleSize, Assert.IsType<CppPrimitiveType>(type.fields.Single(field => field.name == "extendedValue").type).sizeOf);
    }
}
