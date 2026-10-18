using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.Infrastructure.Execution;
using BGCS.Cpp2C.Emission;
using BGCS.Platforms.Toolchains;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeProofTokenTests
{
    [Theory]
    [InlineData("msvc", false, "exact")]
    [InlineData("msvc", false, "offset")]
    [InlineData("msvc", false, "shadow")]
    [InlineData("msvc", true, "exact")]
    [InlineData("msvc", true, "offset")]
    [InlineData("msvc", true, "shadow")]
    [InlineData("clang", false, "exact")]
    [InlineData("clang", false, "offset")]
    [InlineData("clang", false, "shadow")]
    [InlineData("clang", true, "exact")]
    [InlineData("clang", true, "offset")]
    [InlineData("clang", true, "shadow")]
    public async Task CompilerOwnedOffsetsAndProofTokensCannotBeForgedByPublicHeaderMacros(string provider, bool cpp, string scenario)
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Path.Combine(Path.GetTempPath(), "bgcs native proof token " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "native.h"), input = Path.Combine(root, cpp ? "proof.cpp" : "proof.c");
            await File.WriteAllTextAsync(header, "typedef struct Pair { " + (scenario == "offset" ? "int b; int a;" : "int a; int b;") + " } Pair;\n"
                + "#undef offsetof\n#define offsetof(t,m) bgcs_fake_##m\n#define bgcs_fake_a 0\n#define bgcs_fake_b 4\n"
                + (scenario == "shadow" ? "#define " + (cpp ? "static_assert" : "_Static_assert") + "(...)\n" : string.Empty));
            await File.WriteAllTextAsync(input, new NativeStorageAssertionEmitter().Emit(Module(header, cpp), [header]));
            string msvc = MsvcToolchainDiscovery.FindCompiler("x64") ?? throw new FileNotFoundException("MSVC is required for the native storage proof guard gate.");
            MsvcToolchainInfo sdk = MsvcToolchainDiscovery.Resolve(msvc, "x64");
            string compiler = provider == "msvc" ? msvc : NativeConsumerHarness.FindCompiler(cpp);
            ImmutableArray<string> arguments = provider == "msvc"
                ? ["/nologo", cpp ? "/TP" : "/TC", cpp ? "/std:c++latest" : "/std:c17", "/Zs", "/Zc:preprocessor", "/external:W0", input,
                    .. sdk.includeDirectories.Select(directory => "/external:I" + directory)]
                : [cpp ? "-std=c++23" : "-std=c17", "-fsyntax-only", input, .. sdk.includeDirectories.SelectMany(directory => new[] { "-isystem", directory })];
            var compiled = await ProcessExecutor.ExecuteAsync(compiler, arguments, root, TimeSpan.FromSeconds(30));
            Assert.False(compiled.timedOut);
            if (scenario == "exact") Assert.True(compiled.exitCode == 0, compiled.standardOutput + compiled.standardError);
            else
            {
                Assert.NotEqual(0, compiled.exitCode);
                Assert.Contains(scenario == "offset" ? "BGCS native field offset" : "BGCS native storage proof token is shadowed", compiled.standardOutput + compiled.standardError);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static NativeModule Module(string path, bool cpp)
    {
        StableId integer = new("integer"), record = new("pair"), type = new("pair-type"), first = new("a"), second = new("b");
        NativeDeclaration declaration = new(record, "Class", "Pair", "Pair", type, new(path, 1, 1))
        { role = "Struct", isCompleteDefinition = true, isStandardLayout = true, children = [first, second] };
        return new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8,
            [new(integer, NativeTypeCategory.Primitive, "int", 4, 4) { primitiveKind = "Int", isSigned = true },
             new(type, NativeTypeCategory.Record, "Pair", 8, 4) { declaration = record }],
            [declaration,
             new(first, "Field", "a", "Pair::a", integer, new(path, 1, 1)) { parent = record, visibility = "Public", offset = 0, bitOffset = 0 },
             new(second, "Field", "b", "Pair::b", integer, new(path, 1, 1)) { parent = record, visibility = "Public", offset = 4, bitOffset = 32 }], [])
            { language = cpp ? SourceLanguage.Cpp : SourceLanguage.C, byteOrder = NativeByteOrder.LittleEndian };
    }
}
