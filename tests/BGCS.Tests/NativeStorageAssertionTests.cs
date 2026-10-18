using System;
using System.IO;
using System.Linq;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.Contracts.Requests;
using BGCS.Cpp2C.Emission;
using BGCS.Infrastructure.Execution;
using BGCS.Infrastructure.Workers;
using BGCS.Platforms.Targeting;
using BGCS.Platforms.Toolchains;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeStorageAssertionTests
{
    [Theory]
    [InlineData("private-sdk")]
    [InlineData("alias")]
    [InlineData("signature")]
    [InlineData("field")]
    [InlineData("base")]
    [InlineData("template-argument")]
    [InlineData("qualified")]
    public async Task ContextRelativeSdkTemplateValuesRequireAnExternallyNameablePublicContract(string exposure)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-template-storage-proof-" + Guid.NewGuid().ToString("N"));
        string sdk = Path.Combine(root, "sdk");
        Directory.CreateDirectory(sdk);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(sdk, "storage.hpp"), """
                namespace SDK {
                inline constexpr unsigned extent = 3;
                template<unsigned N> struct ExtentStorage { int values[N]; };
                template<> struct ExtentStorage<extent> { int hidden_value; };
                template<class T> class Outer {
                    ExtentStorage<extent> hidden;
                protected:
                    template<class U> struct ProtectedImpl { U value; };
                    ProtectedImpl<T> protected_storage;
                public:
                #ifdef BGCS_PUBLIC_FLOAT
                    float value;
                #else
                    T value;
                #endif
                };
                }
                """);
            string exposed = exposure switch
            {
                "private-sdk" => string.Empty,
                "alias" => "using Exposed = SDK::ExtentStorage<SDK::extent>; Exposed* read_exposed();",
                "signature" => "SDK::ExtentStorage<SDK::extent>* read_exposed();",
                "field" => "struct Exposed { SDK::ExtentStorage<SDK::extent> value; }; Exposed* read_exposed();",
                "base" => "struct Exposed : SDK::ExtentStorage<SDK::extent> {}; Exposed* read_exposed();",
                "template-argument" => "template<class T> struct UserHolder { T value; }; UserHolder<SDK::ExtentStorage<SDK::extent>>* read_exposed();",
                "qualified" => """
                    namespace Public {
                    #ifdef BGCS_LIMIT_DRIFT
                    inline constexpr unsigned extent = 4;
                    #else
                    inline constexpr unsigned extent = 3;
                    #endif
                    template<unsigned N> struct ExtentStorage { int value; };
                    template<> struct ExtentStorage<Public::extent> { int value; };
                    }
                    Public::ExtentStorage<Public::extent>* read_exposed();
                    """,
                _ => throw new ArgumentOutOfRangeException(nameof(exposure))
            };
            string header = Path.Combine(root, "entry.hpp");
            await File.WriteAllTextAsync(header, "#include <storage.hpp>\nstatic_assert(sizeof(SDK::Outer<int>) > 0);\nSDK::Outer<int>* read_outer();\n" + exposed);
            var target = ToolchainResolver.ResolveToolchain(new TargetResolver().Resolve(new(new("host"))), SourceLanguage.Cpp);
            FrontendRequest request = new(target, SourceLanguage.Cpp, [header])
            { pathBase = root, languageStandard = "c++23", systemIncludeDirectories = [sdk], parseMacros = false, parseSystemIncludes = false };
            NativeModule module = (await new FrontendWorkerClient(Path.Combine(WorkerRoot(), "BGCS.Frontend.Clang.Worker.dll"))
                .ParseAsync(request, CancellationToken.None)).module;
            Assert.DoesNotContain(module.diagnostics, diagnostic => diagnostic.isFailure);
            NativeType relative = Assert.Single(module.types, type => type.category == NativeTypeCategory.Record
                && type.declaration is { } declaration && module.GetDeclaration(declaration).qualifiedName == "SDK::ExtentStorage<extent>");
            Assert.True(module.GetDeclaration(relative.declaration!.Value).isSystemDeclaration);
            Assert.Contains(module.declarations, declaration => declaration.name == "ProtectedImpl" && declaration.isCompleteDefinition && declaration.visibility == "Protected");
            if (exposure is not ("private-sdk" or "qualified"))
            {
                NotSupportedException failure = Assert.Throws<NotSupportedException>(() => new NativeStorageAssertionEmitter().Emit(module, [header]));
                Assert.Contains("publicly exposed native type", failure.Message);
                Assert.Contains(relative.spelling, failure.Message);
                return;
            }
            string proof = new NativeStorageAssertionEmitter().Emit(module, [header]);
            Assert.Contains("sizeof(SDK::Outer<int>)", proof);
            Assert.Contains("alignof(SDK::Outer<int>)", proof);
            NativeDeclaration outer = Assert.Single(module.declarations, declaration => declaration.qualifiedName == "SDK::Outer<int>" && declaration.isCompleteDefinition);
            if (outer.isStandardLayout == true)
                Assert.Contains("std::is_standard_layout<SDK::Outer<int>>::value == 1", proof);
            else Assert.DoesNotContain("std::is_standard_layout<SDK::Outer<int>>", proof);
            Assert.Contains("decltype(((SDK::Outer<int>*)nullptr)->value)", proof);
            Assert.DoesNotContain("sizeof(" + relative.spelling + ")", proof);
            Assert.DoesNotContain("ProtectedImpl", proof);
            string source = Path.Combine(root, "proof.cpp");
            await File.WriteAllTextAsync(source, proof);
            ImmutableArray<string> arguments = ["--no-default-config", "--target=" + target.triple, "-std=c++23", "-fsyntax-only", source,
                .. target.toolchain.arguments,
                .. (target.toolchain.sysRoot is string sysroot ? new[] { "--sysroot=" + sysroot } : []), "-isystem", sdk,
                .. target.toolchain.systemIncludeFolders.Concat(target.toolchain.cxxSystemIncludeFolders).SelectMany(directory => new[] { "-isystem", directory })];
            var compiled = await ProcessExecutor.ExecuteAsync(NativeConsumerHarness.FindCompiler(true), arguments, root, TimeSpan.FromSeconds(60));
            Assert.False(compiled.timedOut);
            Assert.True(compiled.exitCode == 0, compiled.standardOutput + compiled.standardError);
            var drift = await ProcessExecutor.ExecuteAsync(NativeConsumerHarness.FindCompiler(true), arguments.Add("-DBGCS_PUBLIC_FLOAT=1"), root, TimeSpan.FromSeconds(60));
            Assert.False(drift.timedOut);
            Assert.NotEqual(0, drift.exitCode);
            Assert.Contains("BGCS native field type differs", drift.standardOutput + drift.standardError);
            if (exposure == "qualified")
            {
                NativeDeclaration witness = Assert.Single(module.declarations, declaration => declaration.qualifiedName == "Public::extent");
                Assert.Equal("Variable", witness.role);
                Assert.Equal("3", witness.constantValue!.value);
                Assert.Equal(EvidenceLevel.Declared, witness.constantValue.evidence.level);
                Assert.Contains("static_assert((Public::extent) == (3ULL)", proof);
                var valueDrift = await ProcessExecutor.ExecuteAsync(NativeConsumerHarness.FindCompiler(true), arguments.Add("-DBGCS_LIMIT_DRIFT=1"), root, TimeSpan.FromSeconds(60));
                Assert.False(valueDrift.timedOut);
                Assert.NotEqual(0, valueDrift.exitCode);
                Assert.Contains("BGCS native template constant differs", valueDrift.standardOutput + valueDrift.standardError);
                NativeModule unproved = new(module.targetId, module.triple, module.pointerSize, module.types,
                    module.declarations.Select(declaration => declaration.id == witness.id ? declaration with { constantValue = null } : declaration).ToImmutableArray(), module.diagnostics)
                    { language = module.language, byteOrder = module.byteOrder };
                Assert.Throws<NotSupportedException>(() => new NativeStorageAssertionEmitter().Emit(unproved, [header]));
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("opaque-negative")]
    [InlineData("size-drift")]
    [InlineData("required-layout-drift")]
    public async Task OpaqueNegativeLayoutDoesNotRequireTraitEqualityButPositiveStorageAndSizeStillDo(string condition)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-layout-trait-implication-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "native.hpp"), source = Path.Combine(root, "proof.cpp");
            await File.WriteAllTextAsync(header, """
                class OpaqueOwner {
                #ifdef BGCS_OPAQUE_SIZE_DRIFT
                    long long value;
                #else
                    int value;
                #endif
                public: OpaqueOwner() = default;
                };
                struct RequiredStorage {
                    int first;
                #ifdef BGCS_REQUIRED_LAYOUT_DRIFT
                private:
                #endif
                    int second;
                };
                static_assert(sizeof(RequiredStorage) == 2 * sizeof(int));
                OpaqueOwner* BorrowOpaque(OpaqueOwner* value);
                RequiredStorage CopyStorage(RequiredStorage value);
                """);
            var target = NativeConsumerHarness.ParserTarget(SourceLanguage.Cpp);
            NativeModule parsed = (await new FrontendWorkerClient(Path.Combine(WorkerRoot(), "BGCS.Frontend.Clang.Worker.dll"))
                .ParseAsync(new(target, SourceLanguage.Cpp, [header])
                { pathBase = root, languageStandard = "c++23", parseMacros = false, parseSystemIncludes = false }, CancellationToken.None)).module;
            Assert.DoesNotContain(parsed.diagnostics, diagnostic => diagnostic.isFailure);
            NativeDeclaration owner = Assert.Single(parsed.declarations, declaration => declaration.qualifiedName == "OpaqueOwner" && declaration.kind == "Class");
            NativeDeclaration storage = Assert.Single(parsed.declarations, declaration => declaration.qualifiedName == "RequiredStorage" && declaration.kind == "Class");
            Assert.True(owner.isStandardLayout);
            Assert.True(storage.isStandardLayout);
            // Model the real cross-compiler case: the parser denies layout use,
            // while the consumer compiler independently finds this opaque class
            // standard-layout. Its representation still never crosses by value.
            NativeModule observed = new(parsed.targetId, parsed.triple, parsed.pointerSize, parsed.types,
                parsed.declarations.Select(declaration => declaration.id == owner.id ? declaration with { isStandardLayout = false } : declaration).ToImmutableArray(), parsed.diagnostics)
                { language = parsed.language, byteOrder = parsed.byteOrder };
            Assert.False(observed.GetDeclaration(owner.id).isStandardLayout);
            string proof = new NativeStorageAssertionEmitter().Emit(observed, [header]);
            Assert.DoesNotContain(proof.Split('\n'), line => line.Contains("is_standard_layout<", StringComparison.Ordinal) && line.Contains("OpaqueOwner", StringComparison.Ordinal));
            Assert.Contains(proof.Split('\n'), line => line.Contains("is_standard_layout<", StringComparison.Ordinal) && line.Contains("RequiredStorage", StringComparison.Ordinal) && line.Contains("== 1", StringComparison.Ordinal));
            Assert.Contains(proof.Split('\n'), line => line.Contains("sizeof(", StringComparison.Ordinal) && line.Contains("OpaqueOwner", StringComparison.Ordinal));
            Assert.Contains(proof.Split('\n'), line => line.Contains("alignof(", StringComparison.Ordinal) && line.Contains("OpaqueOwner", StringComparison.Ordinal));
            Assert.Contains("native field offset differs", proof);
            Assert.Contains("native callable signature differs", proof);
            await File.WriteAllTextAsync(source, proof);
            string compiler = NativeConsumerHarness.FindCompiler(true);
            bool msvc = Path.GetFileName(compiler).Equals("cl.exe", StringComparison.OrdinalIgnoreCase);
            ImmutableArray<string> arguments = msvc
                ? ["/nologo", "/Zs", "/TP", "/std:c++latest", source, .. target.toolchain.systemIncludeFolders.Select(directory => "/I" + directory)]
                : ["-std=c++23", "-fsyntax-only", source, .. NativeConsumerHarness.CompilerSdkArguments()];
            if (condition != "opaque-negative")
                arguments = arguments.Add((msvc ? "/D" : "-D") + (condition == "size-drift" ? "BGCS_OPAQUE_SIZE_DRIFT=1" : "BGCS_REQUIRED_LAYOUT_DRIFT=1"));
            var compiled = await ProcessExecutor.ExecuteAsync(compiler, arguments, root, TimeSpan.FromSeconds(60));
            Assert.False(compiled.timedOut);
            if (condition == "opaque-negative") Assert.True(compiled.exitCode == 0, compiled.standardOutput + compiled.standardError);
            else
            {
                Assert.NotEqual(0, compiled.exitCode);
                Assert.Contains(condition == "size-drift" ? "BGCS native storage size differs" : "BGCS native standard-layout trait differs",
                    compiled.standardOutput + compiled.standardError, StringComparison.Ordinal);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MsvcCEnumProofChecksSignednessAndPromotionRatherThanDistinctGenericIdentity(bool correct)
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Path.Combine(Path.GetTempPath(), "bgcs enum native proof " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string compiler = MsvcToolchainDiscovery.FindCompiler("x64") ?? throw new FileNotFoundException("MSVC is required for the Windows enum proof gate.");
            MsvcToolchainInfo sdk = MsvcToolchainDiscovery.Resolve(compiler, "x64");
            string header = Path.Combine(root, "native.h"), source = Path.Combine(root, "proof.c");
            await File.WriteAllTextAsync(header, "typedef enum { Zero=0, One=1 } PublicEnum;\n");
            StableId integer = new("integer"), enumType = new("enum-type"), declarationId = new("enum");
            NativeDeclaration declaration = new(declarationId, "Enum", "PublicEnum", "PublicEnum", enumType, new(header, 1, 1)) { isCompleteDefinition = true };
            NativeModule module = new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8,
                [new(integer, NativeTypeCategory.Primitive, correct ? "int" : "unsigned int", 4, 4) { primitiveKind = correct ? "Int" : "UInt", isSigned = correct },
                 new(enumType, NativeTypeCategory.Enumeration, "PublicEnum", 4, 4) { declaration = declarationId, elementType = integer }], [declaration], [])
                { language = SourceLanguage.C };
            string assertions = new NativeStorageAssertionEmitter().Emit(module, [header]);
            Assert.Contains("sizeof(PublicEnum)", assertions);
            Assert.DoesNotContain("sizeof(enum PublicEnum)", assertions);
            await File.WriteAllTextAsync(source, assertions);
            ImmutableArray<string> arguments = ["/nologo", "/c", "/TC", "/std:c17", "/Zc:preprocessor", "/external:W0", source, "/Fo" + Path.Combine(root, "proof.obj"),
                .. sdk.includeDirectories.Select(directory => "/external:I" + directory)];
            var compiled = await ProcessExecutor.ExecuteAsync(compiler, arguments, root, TimeSpan.FromSeconds(30));
            Assert.False(compiled.timedOut);
            if (correct) Assert.True(compiled.exitCode == 0, compiled.standardOutput + compiled.standardError);
            else
            {
                Assert.NotEqual(0, compiled.exitCode);
                Assert.Contains("BGCS native enum integer signedness", compiled.standardOutput + compiled.standardError);
                Assert.Contains("BGCS native enum integer promotion", compiled.standardOutput + compiled.standardError);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void NestedRecordInAnUnboundTemplateHasNoAddressableStorageAssertion()
    {
        NativeModule module = Module(specialized: false, system: false);

        string source = new NativeStorageAssertionEmitter().Emit(module, []);
        Assert.DoesNotContain("sizeof(Demo::Outer::Nested)", source);
        Assert.DoesNotContain("decltype(((Demo::Outer::Nested*)nullptr)->value)", source);
        Assert.Contains("sizeof(int)", source);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConcreteNestedStorageRetainsExactPublicFieldChecksIncludingSdkFacts(bool system)
    {
        NativeModule module = Module(specialized: true, system);
        string source = new NativeStorageAssertionEmitter().Emit(module, []);
        Assert.Contains("sizeof(Demo::Outer<int>::Nested) == 4", source);
        Assert.Contains("alignof(Demo::Outer<int>::Nested) == 4", source);
        Assert.Contains("typedef int bgcs_verified_field_0;", source);
        Assert.Contains("decltype(((Demo::Outer<int>::Nested*)nullptr)->value)", source);
    }

    private static NativeModule Module(bool specialized, bool system)
    {
        StableId integer = new("int"), parameter = new("T"), owner = new("outer"), nested = new("nested"), field = new("field"), nestedType = new("nested-type");
        string ownerName = specialized ? "Demo::Outer<int>" : "Demo::Outer";
        NativeDeclaration container = new(owner, "Class", "Outer", ownerName, null, new("source.hpp", 1, 1))
        {
            role = "Struct", isCompleteDefinition = specialized, isSystemDeclaration = system, children = [nested],
            templateKind = specialized ? "ClassTemplateSpecialization" : "TemplateClass", templateParameters = specialized ? [] : [parameter]
        };
        NativeDeclaration record = new(nested, "Class", "Nested", ownerName + "::Nested", nestedType, new("source.hpp", 2, 1))
            { role = "Struct", parent = owner, children = [field], isCompleteDefinition = true, isSystemDeclaration = system };
        NativeDeclaration member = new(field, "Field", "value", ownerName + "::Nested::value", integer, new("source.hpp", 3, 1))
            { parent = nested, offset = 0, bitOffset = 0, visibility = "Public", isSystemDeclaration = system };
        return new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8,
            [new(integer, NativeTypeCategory.Primitive, "int", 4, 4) { primitiveKind = "Int", isSigned = true },
             new(parameter, NativeTypeCategory.Template, "T", null, null),
             new(nestedType, NativeTypeCategory.Record, record.qualifiedName, 4, 4) { declaration = nested }],
            [container, record, member], []) { language = SourceLanguage.Cpp };
    }

    private static string WorkerRoot()
    {
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "BindGen-CS.slnx"))) repository = repository.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(repository!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
