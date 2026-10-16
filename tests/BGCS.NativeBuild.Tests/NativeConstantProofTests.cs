using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.Contracts.Requests;
using BGCS.NativeBuild.Planning;
using BGCS.NativeBuild.Validation;
using BGCS.Platforms.Targeting;
using BGCS.Platforms.Toolchains;
using Xunit;

namespace BGCS.NativeBuild.Tests;

public sealed class NativeConstantProofTests
{
    [Fact]
    public void MacroAliasCannotRetargetAnotherFunctionWithAnIdenticalPointerSignature()
    {
        NativeModule expected = AliasModule(actual: false, changed: false);
        new NativeSourceFactComparer().Verify(expected, AliasModule(actual: true, changed: false));
        Assert.True(NativeSourceFactComparer.RequiresVerification(expected));
        string input = new NativeFactInputEmitter().Emit(["native.h"], module: expected);
        Assert.Contains("(CALL_ALIAS)", input);
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => new NativeSourceFactComparer().Verify(expected, AliasModule(actual: true, changed: true)));
        Assert.Contains("exact function alias target identity", failure.Message);
    }

    [Theory]
    [InlineData("code-unit")]
    [InlineData("width")]
    [InlineData("kind")]
    public void ActualPreprocessingMustPreserveStringValueKindAndWidth(string difference)
    {
        NativeModule expected = StringModule(actual: false);
        NativeModule actual = StringModule(actual: true);
        new NativeSourceFactComparer().Verify(expected, actual);
        NativeDeclaration declaration = actual.declarations[0];
        NativeStringConstant value = declaration.stringConstant!;
        value = difference switch { "code-unit" => value with { codeUnits = [65, 0] }, "width" => value with { unitWidth = 2 }, _ => value with { kind = NativeStringLiteralKind.Utf8 } };
        actual = Clone(actual, [declaration with { stringConstant = value }]);
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => new NativeSourceFactComparer().Verify(expected, actual));
        Assert.Contains("string literal category, exact code units", failure.Message);
    }

    [Fact]
    public void LiteralProofUsesActualExpandedTokensAndRefusesExpectedByteReconstruction()
    {
        NativeModule expected = StringModule(actual: false);
        NativeModule actual = StringModule(actual: true);
        string proof = new NativeStringAssertionEmitter().Emit(expected, actual)!;
        Assert.Contains("(\"é\")", proof);
        Assert.Contains("195ULL", proof);
        Assert.Contains("169ULL", proof);
        Assert.Contains("[2]", proof);
        NativeDeclaration declaration = actual.declarations[0];
        actual = Clone(actual, [declaration with { stringConstant = declaration.stringConstant! with { literalTokens = [] } }]);
        Assert.Throws<NotSupportedException>(() => new NativeStringAssertionEmitter().Emit(expected, actual));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualMsvcExecutionCharsetMustMatchEveryGeneratedStringByte(bool differentCharset)
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Path.Combine(Path.GetTempPath(), "bgcs native literal proof " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string compiler = MsvcToolchainDiscovery.FindCompiler("x64") ?? throw new FileNotFoundException("MSVC is required for the native execution charset gate.");
            var target = new TargetResolver().Resolve(new(new("windows-x64-msvc"), new(compilerPath: compiler)));
            string original = Path.Combine(root, "original.c"), forced = Path.Combine(root, "forced.h");
            await File.WriteAllTextAsync(original, "/* original preprocessing input */");
            await File.WriteAllTextAsync(forced, "#ifdef __cplusplus\n#error original C headers must not be reparsed in the C++ literal proof\n#endif\n");
            NativeBuildPlan plan = new("msvc", target, [], Path.Combine(root, "unused.obj"))
                { scratchDirectory = root, compilerFamily = "msvc", compilerPath = compiler };
            NativeBuildCommand command = new("preprocess", compiler, ["/nologo", "/TC", "/std:c17", "/P", "/Fi" + Path.Combine(root, "original.preprocessed.c"),
                "/source-charset:utf-8", "/execution-charset:" + (differentCharset ? "windows-1252" : "utf-8"), "/FI" + forced, original], root);
            NativePreprocessingOperation operation = new(command, original, SourceLanguage.C, "c17", Path.Combine(root, "original.preprocessed.c"));
            if (differentCharset)
            {
                InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(() => new NativeStringCompilerVerifier().VerifyAsync(plan, operation,
                    StringModule(actual: false), StringModule(actual: true)).AsTask());
                Assert.Contains("BGCS native execution-character", failure.Message);
                Assert.Contains("differs from generated constants", failure.Message);
            }
            else
            {
                NativeStringProofReceipt receipt = Assert.IsType<NativeStringProofReceipt>(await new NativeStringCompilerVerifier().VerifyAsync(plan, operation,
                    StringModule(actual: false), StringModule(actual: true)));
                Assert.Equal(0, receipt.execution.exitCode);
                Assert.False(receipt.execution.timedOut);
                Assert.Equal(compiler, receipt.command.executable);
                Assert.Contains("/execution-charset:utf-8", receipt.command.arguments);
                Assert.DoesNotContain(receipt.command.arguments, argument => argument.StartsWith("/FI", StringComparison.Ordinal));
                Assert.Equal("greeting", Assert.Single(receipt.verifiedConstants).value);
                Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(receipt.source))), receipt.sourceDigest.value);
                Assert.Contains("(\"é\")", receipt.source);
                Assert.False(File.Exists(Assert.Single(receipt.command.inputFiles)));
            }
            Assert.Empty(Directory.GetFiles(root, "literal-proof-*"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static NativeModule AliasModule(bool actual, bool changed)
    {
        string prefix = actual ? "actual:" : "expected:";
        StableId integer = new(prefix + "int"), first = new(prefix + "one"), second = new(prefix + "two"), alias = new("alias");
        NativeDeclaration firstFunction = new(first, "Function", "first", "first", null, new("native.h", 1, 1)) { role = "Function", returnType = integer, callingConvention = "C" };
        NativeDeclaration secondFunction = new(second, "Function", "second", "second", null, new("native.h", 2, 1)) { role = "Function", returnType = integer, callingConvention = "C" };
        NativeDeclaration observed = actual
            ? new(new("probe"), "Field", ProbeName(alias), ProbeName(alias), null, new("expanded.c", 1, 1)) { role = "Variable", functionReference = changed ? second : first }
            : new(alias, "Macro", "CALL_ALIAS", "CALL_ALIAS", null, new("native.h", 3, 1))
                { macro = new(false, [], true) { functionAlias = first, functionAliasEvidence = new(EvidenceLevel.Declared, "compiler:reference") } };
        return new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8, [new(integer, NativeTypeCategory.Primitive, "int", 4, 4) { primitiveKind = "Int", isSigned = true }],
            [firstFunction, secondFunction, observed], []) { language = SourceLanguage.C };
    }

    private static NativeModule StringModule(bool actual)
    {
        StableId identity = new("greeting");
        NativeStringConstant value = new(NativeStringLiteralKind.Ordinary, [195, 169, 0], 1, new(EvidenceLevel.Declared, "compiler:string"))
            { literalTokens = ["\"é\""] };
        NativeDeclaration constant = actual
            ? new(new("probe"), "Field", ProbeName(identity), ProbeName(identity), null, new("expanded.c", 1, 1)) { role = "Variable", stringConstant = value }
            : new(identity, "Macro", "GREETING", "GREETING", null, new("native.h", 1, 1)) { macro = new(false, [], true), stringConstant = value };
        return new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8, [], [constant], []) { language = SourceLanguage.C };
    }

    private static string ProbeName(StableId identity) => "bgcs_native_scalar_fact_" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity.value)));
    private static NativeModule Clone(NativeModule module, ImmutableArray<NativeDeclaration> declarations)
        => new(module.targetId, module.triple, module.pointerSize, module.types, declarations, module.diagnostics) { language = module.language, byteOrder = module.byteOrder };
}
