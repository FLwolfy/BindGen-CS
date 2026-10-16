using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.Frontend.Clang.Advanced;
using BGCS.Infrastructure.IO;
using Xunit;

namespace BGCS.Frontend.Clang.Tests;

public sealed class DeclarationPreludeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicFrontendOwnsPchAndPreservesFrozenIdentifiersAndDetachedSdkFacts(bool retainBodies)
    {
        using Fixture fixture = new();
        await File.WriteAllTextAsync(fixture.header, """
            #ifndef SDK_GUARD
            #define SDK_GUARD
            #define SDK_OBJECT 123
            #define SDK_FUNCTION(x) ((x) + 1)
            #define SDK_SELF SDK_SELF + 1
            #define SAVED 1
            #pragma push_macro("SAVED")
            #undef SAVED
            #define SAVED 2
            #pragma pop_macro("SAVED")
            namespace sdk {
                template<class T> struct Box { T value; };
                struct Unrelated { int hidden; };
                int unrelated_api();
            }
            #endif
            """);
        await File.WriteAllTextAsync(fixture.entry, """
            #if defined(SDK_GUARD) || defined(SDK_OBJECT) || defined(SDK_FUNCTION) || defined(SDK_SELF) || defined(SAVED) || defined(__COUNTER__)
            #error leaked prelude macro
            #endif
            struct FrozenIdentifiers {
                int SDK_OBJECT;
                int SDK_SELF;
                int SAVED;
                int __COUNTER__;
                int __LINE__;
                int __FILE__;
            };
            int SDK_FUNCTION(int value);
            using Result = sdk::Box<int>;
            static_assert(sizeof(Result) == sizeof(int));
            Result transform(Result input);
            #define NATIVE_FINAL 42
            """);
        FrontendRequest request = fixture.Request() with { retainFunctionBodies = retainBodies, parseMacros = true };
        ClangFrontend frontend = new();
        FrontendResult result = await frontend.ParseAsync(request, CancellationToken.None);
        Assert.DoesNotContain(result.module.diagnostics, diagnostic => diagnostic.isFailure);
        Assert.True(result.requiresNativeCompilerProof);
        NativeDeclaration frozen = Assert.Single(result.module.declarations, declaration => declaration.name == "FrozenIdentifiers");
        Assert.Equal(new[] { "SDK_OBJECT", "SDK_SELF", "SAVED", "__COUNTER__", "__LINE__", "__FILE__" },
            frozen.children.Select(result.module.GetDeclaration).Where(declaration => declaration.kind == "Field").Select(declaration => declaration.name));
        Assert.Contains(result.module.declarations, declaration => declaration.name == "SDK_FUNCTION" && declaration.kind == "Function");
        NativeDeclaration finalMacro = Assert.Single(result.module.declarations, declaration => declaration.name == "NATIVE_FINAL");
        Assert.Equal("42", finalMacro.value);
        Assert.True(finalMacro.macro?.isActive, finalMacro.ToString());
        Assert.Equal("42", finalMacro.constantValue?.value);
        NativeDeclaration box = Assert.Single(result.module.declarations, declaration => declaration.kind == "Class" && declaration.qualifiedName == "sdk::Box<int>");
        Assert.True(box.isSystemDeclaration);
        Assert.True(box.isCompleteDefinition);
        Assert.Equal(4, result.module.GetType(box.type!.Value).size);
        NativeDeclaration field = Assert.Single(box.children.Select(result.module.GetDeclaration), declaration => declaration.kind == "Field");
        Assert.Equal("value", field.name);
        Assert.Equal(0, field.offset);
        Assert.Equal("sdk/dependency.hpp", field.source.file);
        Assert.DoesNotContain(result.module.declarations, declaration => declaration.name is "Unrelated" or "unrelated_api");
        Assert.Contains(result.snapshot.files, input => input.path == fixture.header);
        Assert.Contains(result.snapshot.files, input => input.path == fixture.entry);
        Assert.DoesNotContain(result.snapshot.files, input => input.path.Contains("bgcs-clang-prelude-", StringComparison.Ordinal));
        Assert.True(SourceSnapshotVerifier.Matches(result.snapshot));
        FrontendResult repeat = await frontend.ParseAsync(request, CancellationToken.None);
        Assert.Equal(result.module.declarations.Select(declaration => declaration.id), repeat.module.declarations.Select(declaration => declaration.id));
        await File.AppendAllTextAsync(fixture.header, "\n// SDK bytes changed\n");
        Assert.False(SourceSnapshotVerifier.Matches(result.snapshot));
    }

    [Fact]
    public async Task OwnedPchReplayPreservesCompilerTraitsExceptionsAddressabilityAndLiteralUnits()
    {
        using Fixture fixture = new();
        await File.WriteAllTextAsync(fixture.header, "struct Sdk { int value; };\n");
        await File.WriteAllTextAsync(fixture.entry, """
            struct Constructible { int value; };
            struct Deleted { Deleted() = delete; };
            int ComputedNoThrow() noexcept(sizeof(Sdk) == 4);
            int ComputedThrowing() noexcept(sizeof(Sdk) == 0);
            extern "C" void _ReadWriteBarrier(void);
            #pragma intrinsic(_ReadWriteBarrier)
            extern "C" void* memcpy(void* output, const void* input, decltype(sizeof(0)) length);
            static const char ExactBytes[4] = "A\0B";
            static const char PaddedBytes[6] = "A\0B";
            wchar_t WideValue;
            """);
        FrontendRequest request = fixture.Request() with { target = new(new("prelude-replay"), "windows", "x64", "msvc", "x86_64-pc-windows-msvc", new()) };
        NativeModule module = (await new ClangFrontend().ParseAsync(request, CancellationToken.None)).module;
        Assert.DoesNotContain(module.diagnostics, diagnostic => diagnostic.isFailure);
        Assert.True(Record("Constructible").isDefaultConstructible);
        Assert.True(Record("Constructible").isStandardLayout);
        Assert.False(Record("Deleted").isDefaultConstructible);
        Assert.True(Declaration("ComputedNoThrow").isNoThrow);
        Assert.False(Declaration("ComputedThrowing").isNoThrow);
        Assert.Equal(NativeCallableAddressability.DirectCallOnly, Declaration("_ReadWriteBarrier").addressability);
        Assert.Equal(NativeCallableAddressability.Addressable, Declaration("memcpy").addressability);
        Assert.Equal(new uint[] { 65, 0, 66, 0 }, Declaration("ExactBytes").stringConstant!.codeUnits.ToArray());
        Assert.Null(Declaration("PaddedBytes").stringConstant);
        Assert.False(Assert.Single(module.types, type => type.primitiveKind == "WChar").isSigned);
        NativeDeclaration Record(string name) => Assert.Single(module.declarations, declaration => declaration.kind == "Class" && declaration.name == name);
        NativeDeclaration Declaration(string name) => Assert.Single(module.declarations, declaration => declaration.name == name);
    }

    [Fact]
    public async Task OwnedPchMacroReplaySeparatesActiveDefinitionsFromInvalidExpressionsAndUndefs()
    {
        using Fixture fixture = new();
        await File.WriteAllTextAsync(fixture.entry, """
            #define NOT_A_VALUE void
            #define ACTIVE_FUNCTION(x) ((x) + 1)
            #define REMOVED 7
            #undef REMOVED
            #define VALID 42
            """);
        NativeModule module = (await new ClangFrontend().ParseAsync(fixture.Request(), CancellationToken.None)).module;
        Assert.DoesNotContain(module.diagnostics, diagnostic => diagnostic.isFailure);
        Assert.True(Declaration("NOT_A_VALUE").macro!.isActive);
        Assert.Null(Declaration("NOT_A_VALUE").constantValue);
        Assert.True(Declaration("ACTIVE_FUNCTION").macro!.isActive);
        Assert.Null(Declaration("ACTIVE_FUNCTION").constantValue);
        Assert.False(Declaration("REMOVED").macro!.isActive);
        Assert.Null(Declaration("REMOVED").constantValue);
        Assert.True(Declaration("VALID").macro!.isActive);
        Assert.Equal("42", Declaration("VALID").constantValue!.value);
        NativeDeclaration Declaration(string name) => Assert.Single(module.declarations, declaration => declaration.name == name);
    }

    [Fact]
    public async Task GeneratedBuiltinCleanupDoesNotSuppressUserWarningAsError()
    {
        using Fixture fixture = new();
        await File.WriteAllTextAsync(fixture.header, "#define SDK_OBJECT 7\nstruct Sdk { int value; };\n");
        await File.WriteAllTextAsync(fixture.entry, "#warning BGCS_REAL_USER_WARNING\nSdk use(Sdk value);\n");
        FrontendResult ordinary = await new ClangFrontend().ParseAsync(fixture.Request(), CancellationToken.None);
        Assert.Contains(ordinary.module.diagnostics, diagnostic => diagnostic.message.Contains("BGCS_REAL_USER_WARNING", StringComparison.Ordinal));
        FrontendResult strict = await new ClangFrontend().ParseAsync(fixture.Request() with { arguments = ["-Werror"] }, CancellationToken.None);
        Assert.Contains(strict.module.diagnostics, diagnostic => diagnostic.isFailure && diagnostic.message.Contains("BGCS_REAL_USER_WARNING", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AmbiguousSystemBasenamesCannotChangeTheAuthenticatedEntryOrder()
    {
        using Fixture fixture = new();
        string second = Path.Combine(fixture.root, "second", "dependency.hpp");
        Directory.CreateDirectory(Path.GetDirectoryName(second)!);
        await File.WriteAllTextAsync(fixture.header, "#pragma once\nstruct FirstSdk {};\n");
        await File.WriteAllTextAsync(second, "#pragma once\nstruct SecondSdk {};\n");
        FrontendRequest request = fixture.Request() with { declarationPrelude = new(
            "#include \"" + fixture.header.Replace("\\", "\\\\", StringComparison.Ordinal) + "\"\n#include \""
            + second.Replace("\\", "\\\\", StringComparison.Ordinal) + "\"\n", [fixture.header, second]) };
        Assert.Contains("ordered dependency entry", Assert.Throws<IOException>(() =>
            new ClangFrontend().ParseAsync(request, CancellationToken.None).GetAwaiter().GetResult()).Message);
    }

    [Fact]
    public async Task LiveContributorCannotChangePchDependenciesBeforeDetachment()
    {
        using Fixture fixture = new();
        await File.WriteAllTextAsync(fixture.header, "struct Sdk { int value; };\n");
        await File.WriteAllTextAsync(fixture.entry, "Sdk use(Sdk value);\n");
        Assert.Throws<IOException>(() => new ClangFrontend().ParseAsync(fixture.Request(), [new Mutator(fixture.header)], CancellationToken.None).GetAwaiter().GetResult());
    }

    [Fact]
    public async Task InvalidPreludeReportsItsActualCompilerError()
    {
        using Fixture fixture = new();
        await File.WriteAllTextAsync(fixture.header, "#error BGCS_BAD_SDK\n");
        await File.WriteAllTextAsync(fixture.entry, "int original();\n");
        Assert.Contains("BGCS_BAD_SDK", Assert.Throws<InvalidOperationException>(() =>
            new ClangFrontend().ParseAsync(fixture.Request(), CancellationToken.None).GetAwaiter().GetResult()).Message);
    }

    [Fact]
    public void CancelledPublicRequestDoesNotBeginPreludeMaterialization()
    {
        using Fixture fixture = new();
        using CancellationTokenSource cancellation = new(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => new ClangFrontend().ParseAsync(fixture.Request(), cancellation.Token).GetAwaiter().GetResult());
    }

    private sealed class Mutator(string header) : IClangFactContributor
    {
        public NativeModule Contribute(ClangFactContributionContext context, NativeModule module, CancellationToken cancellationToken)
        { File.AppendAllText(header, "\n// changed by live contributor\n"); return module; }
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly string root = Path.Combine(Path.GetTempPath(), "bgcs-prelude-public-" + Guid.NewGuid().ToString("N"));
        internal readonly string header, entry;
        internal Fixture()
        {
            Directory.CreateDirectory(Path.Combine(root, "sdk"));
            header = Path.Combine(root, "sdk", "dependency.hpp"); entry = Path.Combine(root, "frozen.hpp");
            File.WriteAllText(header, "struct Sdk {};\n"); File.WriteAllText(entry, "int original();\n");
        }
        internal FrontendRequest Request()
            => new(new NativeTargetDescriptor(new("prelude"), "test", "test", "test", "x86_64-pc-linux-gnu", new()), SourceLanguage.Cpp, [entry])
            {
                pathBase = root, parseSystemIncludes = false,
                declarationPrelude = new("#include \"" + header.Replace("\\", "\\\\", StringComparison.Ordinal) + "\"\n", [header])
            };
        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
