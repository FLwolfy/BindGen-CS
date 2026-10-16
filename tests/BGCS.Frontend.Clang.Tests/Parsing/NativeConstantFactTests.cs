using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.Contracts.Requests;
using BGCS.Frontend.Clang;
using BGCS.Platforms.Targeting;
using Xunit;

namespace BGCS.Frontend.Clang.Tests;

public sealed class NativeConstantFactTests
{
    [Theory]
    [InlineData(SourceLanguage.C)]
    [InlineData(SourceLanguage.Cpp)]
    public async Task HiddenSdkAndCommandLineContextAliasesNeverBecomeProbeSiteConstants(SourceLanguage language)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs hidden macro context " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string sdk = Path.Combine(root, "sdk");
            Directory.CreateDirectory(sdk);
            await File.WriteAllTextAsync(Path.Combine(sdk, "context.h"), """
                #define SDK_CONTEXT __LINE__
                #define SDK_COUNTER(value) ((value) + __COUNTER__)
                #define SDK_STABLE(value) ((value) + 40)
                #define SDK_LITERAL_SIZE sizeof("__LINE__")
                """);
            string header = Path.Combine(root, "public.h");
            await File.WriteAllTextAsync(header, """
                #include <context.h>
                #define PUBLIC_LINE SDK_CONTEXT
                #define PUBLIC_COUNTER SDK_COUNTER(1)
                #define PUBLIC_COMMAND_LINE COMMAND_CONTEXT
                #define PUBLIC_STABLE SDK_STABLE(2)
                #define PUBLIC_COMMAND_STABLE COMMAND_STABLE
                #define PUBLIC_LITERAL_SIZE SDK_LITERAL_SIZE
                """);
            FrontendRequest request = new(new TargetResolver().Resolve(new(new("host"))), language, [header])
            { pathBase = root, parseMacros = true, systemIncludeDirectories = [sdk], defines = ["COMMAND_CONTEXT=__LINE__", "COMMAND_STABLE=43"] };
            NativeModule module = (await new ClangFrontend().ParseAsync(request, CancellationToken.None)).module;
            Assert.DoesNotContain(module.diagnostics, diagnostic => diagnostic.isFailure);
            foreach (string name in new[] { "PUBLIC_LINE", "PUBLIC_COUNTER", "PUBLIC_COMMAND_LINE" })
                Assert.Null(Assert.Single(module.declarations, declaration => declaration.name == name).constantValue);
            Assert.Equal("42", Assert.Single(module.declarations, declaration => declaration.name == "PUBLIC_STABLE").constantValue!.value);
            Assert.Equal("43", Assert.Single(module.declarations, declaration => declaration.name == "PUBLIC_COMMAND_STABLE").constantValue!.value);
            Assert.Equal("9", Assert.Single(module.declarations, declaration => declaration.name == "PUBLIC_LITERAL_SIZE").constantValue!.value);
            Assert.DoesNotContain(module.declarations, declaration => declaration.name.StartsWith("SDK_", StringComparison.Ordinal) || declaration.name.StartsWith("COMMAND_", StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(SourceLanguage.C)]
    [InlineData(SourceLanguage.Cpp)]
    public async Task CompilerEvaluationPreservesExpressionTypesActiveDefinitionsAndExactScalarBits(SourceLanguage language)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs compiler constants " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "constants.h");
            await File.WriteAllTextAsync(header, """
                #define MAXIMUM 18446744073709551615ULL
                #define SHIFTED ((1ULL << 63) | 17ULL)
                #define MINIMUM (-9223372036854775807LL - 1LL)
                #define NEGATIVE_ZERO (-0.0f)
                #define HALF (1.0 / 2.0)
                #define NATIVE_LONG 0xffffffffUL
                #define BAD_TYPE unsigned int
                #define STATEMENT {
                #define CONTEXT __LINE__
                #define INDIRECT_CONTEXT CONTEXT
                #define APPLY(value) ((value) + 1)
                #define REPLACED 1
                #undef REPLACED
                #define REPLACED 23
                #define REMOVED 7
                #undef REMOVED
                static const unsigned long long field_value = SHIFTED;
                int mutable_value = 42;
                """);
            FrontendRequest request = new(new TargetResolver().Resolve(new(new("host"))), language, [header])
            { pathBase = root, parseMacros = true };
            NativeModule module = (await new ClangFrontend().ParseAsync(request, CancellationToken.None)).module;
            Assert.DoesNotContain(module.diagnostics, diagnostic => diagnostic.isFailure);
            NativeScalarConstant Maximum = Value("MAXIMUM");
            Assert.Equal(NativeScalarConstantKind.Integer, Maximum.kind);
            Assert.Equal("18446744073709551615", Maximum.value);
            Assert.Equal(8, Maximum.width);
            Assert.False(Maximum.isSigned);
            Assert.Equal("9223372036854775825", Value("SHIFTED").value);
            Assert.Equal("-9223372036854775808", Value("MINIMUM").value);
            Assert.Equal("80000000", Value("NEGATIVE_ZERO").value);
            Assert.Equal("3FE0000000000000", Value("HALF").value);
            Assert.Equal(OperatingSystem.IsWindows() ? 4 : 8, Value("NATIVE_LONG").width);
            Assert.Equal(Value("SHIFTED").value, Value("field_value").value);
            Assert.Null(Assert.Single(module.declarations, declaration => declaration.name == "mutable_value").constantValue);
            foreach (string omitted in new[] { "BAD_TYPE", "STATEMENT", "CONTEXT", "INDIRECT_CONTEXT", "APPLY", "REMOVED" })
                Assert.All(module.declarations.Where(declaration => declaration.name == omitted), declaration => Assert.Null(declaration.constantValue));
            NativeDeclaration replaced = Assert.Single(module.declarations, declaration => declaration.name == "REPLACED" && declaration.macro?.isActive == true);
            Assert.Equal("23", replaced.constantValue!.value);
            Assert.False(Assert.Single(module.declarations, declaration => declaration.name == "REMOVED").macro!.isActive);
            NativeMacroDefinition applied = Assert.Single(module.declarations, declaration => declaration.name == "APPLY").macro!;
            Assert.True(applied.isFunctionLike);
            Assert.Equal("value", Assert.Single(applied.parameters));
            Assert.DoesNotContain(module.declarations, declaration => declaration.name.StartsWith("bgcs_constant_probe_", StringComparison.Ordinal));

            NativeScalarConstant Value(string name) => Assert.Single(module.declarations,
                declaration => declaration.name == name && declaration.constantValue is not null).constantValue!;
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
