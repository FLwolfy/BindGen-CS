using System;
using System.IO;
using System.Linq;
using System.Text;
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

    [Theory]
    [InlineData("protected", "Protected")]
    [InlineData("private", "Private")]
    [InlineData("public", "Public")]
    public async Task InstantiatedNestedSdkTemplatePreservesItsDeclaredProtectedAccess(string access, string expected)
    {
        using Fixture fixture = new();
        await File.WriteAllTextAsync(fixture.header, $$"""
            namespace sdk_access {
            template<class T> struct Owner {
            {{access}}:
                template<class U> struct Hidden { U value; };
            public:
                Hidden<T> data;
            };
            }
            """);
        await File.WriteAllTextAsync(fixture.entry, """
            using SelectedOwner = sdk_access::Owner<int>;
            static_assert(sizeof(SelectedOwner) == sizeof(int));
            SelectedOwner transform(SelectedOwner value);
            """);
        FrontendResult result = await new ClangFrontend().ParseAsync(fixture.Request(), CancellationToken.None);
        Assert.DoesNotContain(result.module.diagnostics, diagnostic => diagnostic.isFailure);
        NativeDeclaration owner = Assert.Single(result.module.declarations,
            declaration => declaration.qualifiedName == "sdk_access::Owner<int>");
        NativeDeclaration data = Assert.Single(owner.children.Select(result.module.GetDeclaration), declaration => declaration.name == "data");
        NativeType hiddenType = result.module.GetType(data.type!.Value);
        NativeDeclaration hidden = result.module.GetDeclaration(hiddenType.declaration!.Value);
        Assert.True(hidden.isSystemDeclaration);
        Assert.True(hidden.isCompleteDefinition);
        Assert.Equal(expected, hidden.visibility);
        Assert.Equal(4, hiddenType.size);
        Assert.Contains(result.snapshot.files, input => input.path == fixture.header);
        Assert.True(SourceSnapshotVerifier.Matches(result.snapshot));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitTemplateSelfNamesResolveToTheirOwnersWithoutDuplicateNestedRecords(bool usePrelude)
    {
        using Fixture fixture = new();
        await File.WriteAllTextAsync(fixture.entry, """
            namespace Demo {
            template<class T> class Selector {
            public: Selector() = default; Selector* Self() { return this; }
            };
            template<class T> class Selector<T*> {
            public: Selector() = default; Selector* Self() { return this; }
            };
            template<> class Selector<int> {
            public: Selector() = default; Selector* Self() { return this; }
            };
            }
            """);
        FrontendRequest request = fixture.Request() with
        {
            declarationPrelude = usePrelude ? fixture.Request().declarationPrelude : null,
            postamble = "template class Demo::Selector<int>;\ntemplate class Demo::Selector<float*>;\n"
        };
        NativeModule module = (await new ClangFrontend().ParseAsync(request, CancellationToken.None)).module;
        Assert.DoesNotContain(module.diagnostics, diagnostic => diagnostic.isFailure);
        Assert.DoesNotContain(module.declarations, declaration => declaration.kind == "Class" && declaration.qualifiedName == "Demo::Selector::Selector");
        foreach (string name in new[] { "Demo::Selector<int>", "Demo::Selector<float *>" })
        {
            NativeDeclaration owner = Assert.Single(module.declarations, declaration => declaration.kind == "Class" && declaration.qualifiedName == name);
            NativeType type = Assert.Single(module.types, type => type.category == NativeTypeCategory.Record && type.declaration == owner.id);
            Assert.Equal(owner.type, type.id);
            NativeDeclaration self = Assert.Single(owner.children.Select(module.GetDeclaration), declaration => declaration.name == "Self");
            NativeType pointer = module.GetType(self.returnType!.Value);
            Assert.Equal(NativeTypeCategory.Pointer, pointer.category);
            Assert.Equal(type.id, pointer.elementType);
            Assert.DoesNotContain(owner.children.Select(module.GetDeclaration), declaration => declaration.kind == "Class" && declaration.name == "Selector");
        }
    }

    [Fact]
    public async Task ReferencedSdkAliasChainsPreserveCompilerAccessBeforeLexicalMemberTraversal()
    {
        using Fixture fixture = new();
        await File.WriteAllTextAsync(fixture.header, """
            namespace sdk_alias_access {
            template<class T> class Owner {
            private:
                typedef T HiddenTypedef;
                using HiddenUsing = T;
            protected:
                typedef T ProtectedTypedef;
                using ProtectedUsing = T;
            public:
                typedef HiddenTypedef PublicTypedef;
                using PublicUsing = HiddenUsing;
                typedef ProtectedTypedef PublicProtectedTypedef;
                using PublicProtectedUsing = ProtectedUsing;
            };
            }
            """);
        await File.WriteAllTextAsync(fixture.entry, """
            sdk_alias_access::Owner<int>::PublicTypedef typedef_value(sdk_alias_access::Owner<int>::PublicTypedef value);
            sdk_alias_access::Owner<int>::PublicUsing using_value(sdk_alias_access::Owner<int>::PublicUsing value);
            sdk_alias_access::Owner<int>::PublicProtectedTypedef protected_typedef_value(sdk_alias_access::Owner<int>::PublicProtectedTypedef value);
            sdk_alias_access::Owner<int>::PublicProtectedUsing protected_using_value(sdk_alias_access::Owner<int>::PublicProtectedUsing value);
            """);
        FrontendResult result = await new ClangFrontend().ParseAsync(fixture.Request(), CancellationToken.None);
        Assert.DoesNotContain(result.module.diagnostics, diagnostic => diagnostic.isFailure);
        foreach ((string name, string access) in new[]
        {
            ("HiddenTypedef", "Private"), ("HiddenUsing", "Private"),
            ("ProtectedTypedef", "Protected"), ("ProtectedUsing", "Protected"),
            ("PublicTypedef", "Public"), ("PublicUsing", "Public"),
            ("PublicProtectedTypedef", "Public"), ("PublicProtectedUsing", "Public")
        })
        {
            NativeDeclaration alias = Assert.Single(result.module.declarations, declaration => declaration.kind == "Typedef" && declaration.name == name);
            Assert.Equal(access, alias.visibility);
            Assert.True(alias.isSystemDeclaration);
            Assert.Equal("sdk_alias_access::Owner<int>", result.module.GetDeclaration(alias.parent!.Value).qualifiedName);
        }
        Assert.Contains(result.snapshot.files, input => input.path == fixture.header);
        Assert.True(SourceSnapshotVerifier.Matches(result.snapshot));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnonymousAggregateFieldsPreserveCompilerAccessInPrimaryAndInstantiatedTemplates(bool usePrelude)
    {
        using Fixture fixture = new();
        await File.WriteAllTextAsync(fixture.header, """
            namespace aggregate_access {
            template<class T> class Owner {
                union { T member; };
            public: int Read() const { return 0; }
            };
            template<class T> class PublicOwner {
            public: union { T member; };
            private: int Read() const { return 0; }
            };
            }
            """);
        string originalInclude = usePrelude ? string.Empty : "#include \"" + fixture.header.Replace('\\', '/') + "\"\n";
        await File.WriteAllTextAsync(fixture.entry, originalInclude + """
            static_assert(sizeof(aggregate_access::Owner<int>) == sizeof(int));
            static_assert(sizeof(aggregate_access::PublicOwner<int>) == sizeof(int));
            void use(aggregate_access::Owner<int> hidden, aggregate_access::PublicOwner<int> visible);
            """);
        FrontendRequest request = fixture.Request() with { declarationPrelude = usePrelude ? fixture.Request().declarationPrelude : null };
        FrontendResult result = await new ClangFrontend().ParseAsync(request, CancellationToken.None);
        Assert.DoesNotContain(result.module.diagnostics, diagnostic => diagnostic.isFailure);
        foreach ((string name, string access) in new[] { ("Owner", "Private"), ("PublicOwner", "Public") })
        {
            Check(name + "<int>", access);
            if (!usePrelude) Check(name + "<T>", access);
        }
        Assert.Contains(result.snapshot.files, input => input.path == fixture.header);
        Assert.True(SourceSnapshotVerifier.Matches(result.snapshot));

        void Check(string name, string access)
        {
            NativeDeclaration owner = Assert.Single(result.module.declarations,
                declaration => declaration.kind == "Class" && declaration.qualifiedName == "aggregate_access::" + name);
            NativeDeclaration field = Assert.Single(owner.children.Select(result.module.GetDeclaration), declaration => declaration.kind == "Field");
            Assert.Equal(access, field.visibility);
        }
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
    public async Task AuthenticatedSystemEntryPreservesIncludeNextSearchOriginBeforeMacroDiscovery()
    {
        using Fixture fixture = new();
        string cxx = Path.Combine(fixture.root, "cxx");
        string c = Path.Combine(fixture.root, "c");
        Directory.CreateDirectory(cxx);
        Directory.CreateDirectory(c);
        string wrapper = Path.Combine(cxx, "wrapper.hpp");
        string selected = Path.Combine(c, "selected.hpp");
        await File.WriteAllTextAsync(wrapper, "#pragma once\n#include_next <selected.hpp>\n");
        await File.WriteAllTextAsync(Path.Combine(cxx, "selected.hpp"), "#error BGCS_INCLUDE_NEXT_RESTARTED_AT_FIRST_ROOT\n");
        await File.WriteAllTextAsync(selected, "struct SdkNext { int value; };\n");
        await File.WriteAllTextAsync(fixture.entry, "SdkNext transform(SdkNext value);\n");
        FrontendRequest request = fixture.Request() with
        {
            headerSearchPaths = [new(cxx, false, false), new(c, false, false)],
            declarationPrelude = new("#define ENTRY_STATE 42\n#include \"" + wrapper.Replace("\\", "\\\\", StringComparison.Ordinal) + "\"\n", [wrapper])
        };
        FrontendResult result = await new ClangFrontend().ParseAsync(request, CancellationToken.None);
        Assert.DoesNotContain(result.module.diagnostics, diagnostic => diagnostic.isFailure);
        Assert.True(result.requiresNativeCompilerProof);
        NativeDeclaration record = Assert.Single(result.module.declarations, declaration => declaration.name == "SdkNext");
        Assert.True(record.isSystemDeclaration);
        Assert.Equal(4, result.module.GetType(record.type!.Value).size);
        Assert.Contains(result.snapshot.files, input => input.path == wrapper);
        Assert.Contains(result.snapshot.files, input => input.path == selected);
        Assert.DoesNotContain(result.snapshot.files, input => input.path == Path.Combine(cxx, "selected.hpp"));
        Assert.True(SourceSnapshotVerifier.Matches(result.snapshot));
    }

    [Fact]
    public async Task PreludeEntryRewritePreservesIncludeLookingCommentsRawStringsAndContinuedMacros()
    {
        using Fixture fixture = new();
        string include = "#include \"" + fixture.header.Replace("\\", "\\\\", StringComparison.Ordinal) + "\"";
        string literal = "\n" + include + "\n";
        await File.WriteAllTextAsync(fixture.header, "static_assert(ENTRY_STATE == 42 && DIGITS == 1000 && HEX_DIGITS == 0x1ff && WIDE_CHARACTER == L'x');\nstatic_assert(sizeof(JOINED) == 3);\nstatic_assert(sizeof(REPLAY_LITERAL) == "
            + (Encoding.UTF8.GetByteCount(literal) + 1) + ");\nstruct Sdk { int value; };\n");
        await File.WriteAllTextAsync(fixture.entry, "Sdk transform(Sdk value);\n");
        string source = "/*\n" + include + "\n*/\n#define REPLAY_LITERAL u8R\"bgcs(" + literal + ")bgcs\"\n#define CONTINUED \\\n"
            + include + "\n#define DIGITS 1'000\n#define HEX_DIGITS 0x1'ff\n#define WIDE_CHARACTER L'x'\n#define JOINED \"a\\\r\nb\"\n#define ENTRY_STATE 42\n" + include + "\n";
        FrontendResult result = await new ClangFrontend().ParseAsync(fixture.Request() with { declarationPrelude = new(source, [fixture.header]) }, CancellationToken.None);
        Assert.DoesNotContain(result.module.diagnostics, diagnostic => diagnostic.isFailure);
        Assert.True(Assert.Single(result.module.declarations, declaration => declaration.name == "Sdk").isSystemDeclaration);
        Assert.Contains(result.snapshot.files, input => input.path == fixture.header);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unlisted")]
    [InlineData("duplicate")]
    [InlineData("inactive")]
    public async Task PreludeEntryRewriteRequiresEveryDeclaredIncludeToBeActiveAndExactlyOrdered(string shape)
    {
        using Fixture fixture = new();
        string include = "#include \"" + fixture.header.Replace("\\", "\\\\", StringComparison.Ordinal) + "\"\n";
        string other = Path.Combine(fixture.root, "sdk", "other.hpp");
        await File.WriteAllTextAsync(other, "struct Other {};\n");
        string source = shape switch
        {
            "missing" => "#define NO_INCLUDE 1\n",
            "unlisted" => "#include \"" + other.Replace("\\", "\\\\", StringComparison.Ordinal) + "\"\n",
            "duplicate" => include + include,
            "inactive" => "#if 0\n" + include + "#endif\n",
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        IOException failure = Assert.Throws<IOException>(() => new ClangFrontend().ParseAsync(
            fixture.Request() with { declarationPrelude = new(source, [fixture.header]) }, CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("ordered dependency entry", failure.Message);
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
