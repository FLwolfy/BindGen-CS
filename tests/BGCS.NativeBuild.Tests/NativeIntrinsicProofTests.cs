using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.Infrastructure.Execution;
using BGCS.NativeBuild.Planning;
using BGCS.Platforms.Toolchains;
using Xunit;

namespace BGCS.NativeBuild.Tests;

public sealed class NativeIntrinsicProofTests
{
    [Theory]
    [InlineData("msvc", "exact")]
    [InlineData("msvc", "wrong-result")]
    [InlineData("msvc", "wrong-arity")]
    [InlineData("clang", "exact")]
    [InlineData("clang", "wrong-result")]
    [InlineData("clang", "wrong-arity")]
    public async Task DirectVoidIntrinsicProofRequiresActualCompilerReturnAndArityAgreement(string provider, string scenario)
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Path.Combine(Path.GetTempPath(), "bgcs direct intrinsic proof " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "native.h"), source = Path.Combine(root, "proof.c");
            string result = scenario == "wrong-result" ? "int" : "void", parameters = scenario == "wrong-arity" ? "int" : "void";
            await File.WriteAllTextAsync(header, result + " __debugbreak(void);\n#pragma intrinsic(__debugbreak)\nvoid _ReadWriteBarrier(" + parameters + ");\n#pragma intrinsic(_ReadWriteBarrier)\n");
            NativeModule module = Module(header);
            string proof = new NativeStorageAssertionEmitter().Emit(module, [header]);
            Assert.DoesNotContain("&__debugbreak", proof);
            Assert.DoesNotContain("&_ReadWriteBarrier", proof);
            Assert.True(proof.IndexOf("warning(error:4391 4392)", StringComparison.Ordinal) < proof.IndexOf("#include", StringComparison.Ordinal));
            await File.WriteAllTextAsync(source, proof);
            string msvc = MsvcToolchainDiscovery.FindCompiler("x64") ?? throw new FileNotFoundException("MSVC is required for the Windows intrinsic proof gate.");
            MsvcToolchainInfo sdk = MsvcToolchainDiscovery.Resolve(msvc, "x64");
            string compiler = provider == "msvc" ? msvc : NativeBuildToolDiscovery.FindExecutable("clang", "C:/Program Files/LLVM/bin/clang.exe")!;
            ImmutableArray<string> arguments = provider == "msvc"
                ? ["/nologo", "/c", "/TC", "/std:c17", "/Zc:preprocessor", "/external:W0", source, "/Fo" + Path.Combine(root, "proof.obj"), .. sdk.includeDirectories.Select(directory => "/external:I" + directory)]
                : ["-std=c17", "-c", source, "-o", Path.Combine(root, "proof.obj"), .. sdk.includeDirectories.SelectMany(directory => new[] { "-isystem", directory })];
            var compiled = await ProcessExecutor.ExecuteAsync(compiler, arguments, root, TimeSpan.FromSeconds(30));
            Assert.False(compiled.timedOut);
            if (scenario == "exact") Assert.True(compiled.exitCode == 0, compiled.standardOutput + compiled.standardError);
            else Assert.NotEqual(0, compiled.exitCode);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("parameters")]
    [InlineData("result")]
    [InlineData("cpp")]
    [InlineData("sysv-target")]
    [InlineData("calling-convention")]
    [InlineData("missing-evidence")]
    public void UnprovenIntrinsicShapesHaveNoAssumedNativeSignature(string scenario)
    {
        NativeModule module = Module("native.h");
        NativeDeclaration first = module.declarations[0];
        StableId parameter = new("parameter");
        ImmutableArray<NativeDeclaration> declarations = module.declarations;
        if (scenario == "parameters") declarations = declarations.Add(new(parameter, "Parameter", "value", "__debugbreak::value", new("int"), new("native.h", 1, 1)));
        declarations = declarations.SetItem(0, first with
        {
            children = scenario == "parameters" ? [parameter] : [],
            returnType = scenario == "result" ? new("int") : new("void"),
            callingConvention = scenario == "calling-convention" ? "X86VectorCall" : "C",
            addressabilityEvidence = scenario == "missing-evidence" ? null : first.addressabilityEvidence
        });
        module = new(module.targetId, scenario == "sysv-target" ? "x86_64-unknown-linux-gnu" : module.triple,
            module.pointerSize, module.types, declarations, module.diagnostics)
        { language = scenario == "cpp" ? SourceLanguage.Cpp : module.language, byteOrder = module.byteOrder };
        NotSupportedException failure = Assert.Throws<NotSupportedException>(() => new NativeStorageAssertionEmitter().Emit(module, []));
        Assert.Contains("direct-call-only", failure.Message);
    }

    private static NativeModule Module(string header)
    {
        StableId result = new("void"), integer = new("int");
        return new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8,
            [new(result, NativeTypeCategory.Primitive, "void", null, null) { primitiveKind = "Void" }, new(integer, NativeTypeCategory.Primitive, "int", 4, 4) { primitiveKind = "Int", isSigned = true }],
            new[] { "__debugbreak", "_ReadWriteBarrier" }.Select(name => new NativeDeclaration(new(name), "Function", name, name, null, new(header, 1, 1))
            {
                role = "Function", returnType = result, callingConvention = "C", addressability = NativeCallableAddressability.DirectCallOnly,
                addressabilityEvidence = new(EvidenceLevel.Declared, "fixture:compiler-address-expression")
            }).ToImmutableArray(), []) { language = SourceLanguage.C };
    }
}
