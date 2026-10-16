using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.Contracts.Requests;
using BGCS.Infrastructure.Execution;
using BGCS.Infrastructure.Workers;
using BGCS.NativeBuild.Planning;
using BGCS.Platforms.Targeting;
using BGCS.Platforms.Toolchains;
using Xunit;

namespace BGCS.NativeBuild.Tests;

public sealed class NativeCanonicalAssertionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecursiveRecordAliasesAndCallbacksKeepExactNativeProofsRegardlessOfDeclarationOrder(bool recordFirst)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-canonical-recursive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "recursive.hpp"), source = Path.Combine(root, "proof.cpp");
            await File.WriteAllTextAsync(header, """
                struct Node;
                #ifdef BGCS_RECURSIVE_ALIAS_CONST
                using NodePtr = const Node*;
                #else
                using NodePtr = Node*;
                #endif
                using NodePtrAlias = NodePtr;
                using NodeConstPointer = NodePtr const;
                using NodeCallback = void(*)(NodePtrAlias);
                struct Node { NodePtrAlias next; NodeCallback notify; };
                """);
            var target = ToolchainResolver.ResolveToolchain(new TargetResolver().Resolve(new(new("host"))), SourceLanguage.Cpp, "clang");
            NativeModule module = RecursiveModule(target.targetId.value, target.triple, recordFirst);
            string proof = new NativeCanonicalAssertionEmitter().Emit(module, [header]);
            foreach (string alias in new[] { "NodePtr", "NodePtrAlias", "NodeConstPointer", "NodeCallback" })
                Assert.Contains("std::is_same<::" + alias + ",", proof);
            await File.WriteAllTextAsync(source, proof);
            ImmutableArray<string> arguments = ["--no-default-config", "--target=" + target.triple, "-std=c++23", "-fsyntax-only", "-Werror", source,
                .. target.toolchain.arguments,
                .. (target.toolchain.sysRoot is string sysroot ? new[] { "--sysroot=" + sysroot } : []),
                .. target.toolchain.systemIncludeFolders.Concat(target.toolchain.cxxSystemIncludeFolders).SelectMany(directory => new[] { "-isystem", directory })];
            var compiled = await ProcessExecutor.ExecuteAsync(target.toolchain.compilerPath!, arguments, root, TimeSpan.FromSeconds(60));
            Assert.False(compiled.timedOut);
            Assert.True(compiled.exitCode == 0, compiled.standardOutput + compiled.standardError);
            var drift = await ProcessExecutor.ExecuteAsync(target.toolchain.compilerPath!, arguments.Add("-DBGCS_RECURSIVE_ALIAS_CONST=1"), root, TimeSpan.FromSeconds(60));
            Assert.False(drift.timedOut);
            Assert.NotEqual(0, drift.exitCode);
            Assert.Contains("alias canonical type differs", drift.standardOutput + drift.standardError);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ADeclaredRecordDoesNotExcuseAnAliasCycleInsideItsPublicSdkFields()
    {
        NativeModule original = RecursiveModule("proof", "x86_64-unknown-linux-gnu", recordFirst: false);
        StableId first = new("bad-first"), second = new("bad-second"), field = new("bad-field");
        NativeDeclaration record = original.GetDeclaration(new("node-decl"));
        // Only the user alias reaches this SDK declaration. Its public field
        // closure still has to reject the malformed alias-only cycle.
        ImmutableArray<NativeDeclaration> declarations = original.declarations.Select(declaration => declaration.id == record.id
            ? declaration with { children = declaration.children.Add(field), isSystemDeclaration = true } : declaration).ToImmutableArray()
            .Add(new(field, "Field", "broken", "Node::broken", first, new("recursive.hpp", 1, 1))
                { parent = record.id, visibility = "Public", isSystemDeclaration = true });
        ImmutableArray<NativeType> types = original.types
            .Add(new(first, NativeTypeCategory.Alias, "BadFirst", 8, 8) { elementType = second })
            .Add(new(second, NativeTypeCategory.Alias, "BadSecond", 8, 8) { elementType = first });
        NativeModule malformed = new(original.targetId, original.triple, original.pointerSize, types, declarations, []) { language = SourceLanguage.Cpp };
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => new NativeCanonicalAssertionEmitter().Emit(malformed, ["recursive.hpp"]));
        Assert.Equal("A cyclic native type graph prevents canonical verification.", failure.Message);
    }

    [Fact]
    public async Task PrivateSdkAliasFactsStayPrivateWhileReachablePublicAliasesKeepIndependentCanonicalProofs()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-canonical-sdk-alias-" + Guid.NewGuid().ToString("N"));
        string sdk = Path.Combine(root, "sdk");
        Directory.CreateDirectory(sdk);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(sdk, "aliases.hpp"), """
                namespace SDK {
                template<class T> class Box {
                #ifdef BGCS_PRIVATE_ALIAS_FLOAT
                    typedef float HiddenTypedef;
                #else
                    typedef T HiddenTypedef;
                #endif
                    using HiddenUsing = HiddenTypedef;
                    HiddenUsing hidden;
                public:
                    using Value = HiddenUsing;
                    using Independent = T;
                };
                }
                """);
            string header = Path.Combine(root, "entry.hpp");
            await File.WriteAllTextAsync(header, """
                #include <aliases.hpp>
                static_assert(sizeof(SDK::Box<int>) > 0);
                using PublicType = SDK::Box<int>::Value;
                using OtherType = SDK::Box<int>::Independent;
                PublicType read_value();
                OtherType read_other();
                """);
            var target = ToolchainResolver.ResolveToolchain(new TargetResolver().Resolve(new(new("host"))), SourceLanguage.Cpp, "clang");
            FrontendRequest request = new(target, SourceLanguage.Cpp, [header])
            { pathBase = root, languageStandard = "c++23", systemIncludeDirectories = [sdk], parseMacros = false, parseSystemIncludes = false };
            NativeModule module = (await new FrontendWorkerClient(Path.Combine(WorkerRoot(), "BGCS.Frontend.Clang.Worker.dll"))
                .ParseAsync(request, CancellationToken.None)).module;
            Assert.DoesNotContain(module.diagnostics, diagnostic => diagnostic.isFailure);
            foreach (string name in new[] { "HiddenTypedef", "HiddenUsing" })
            {
                NativeDeclaration alias = Assert.Single(module.declarations, declaration => declaration.qualifiedName == "SDK::Box<int>::" + name);
                Assert.True(alias.isSystemDeclaration);
                Assert.Equal("Private", alias.visibility);
                Assert.Equal(NativeTypeCategory.Alias, module.GetType(alias.type!.Value).category);
            }
            NativeDeclaration value = Assert.Single(module.declarations, declaration => declaration.qualifiedName == "SDK::Box<int>::Value");
            Assert.True(value.isSystemDeclaration);
            Assert.Equal("Public", value.visibility);
            string proof = new NativeCanonicalAssertionEmitter().Emit(module, [header]);
            Assert.DoesNotContain("::HiddenTypedef", proof);
            Assert.DoesNotContain("::HiddenUsing", proof);
            Assert.Contains("std::is_same<::SDK::Box<int>::Value,", proof);
            Assert.Contains("std::is_same<::SDK::Box<int>::Independent,", proof);
            Assert.Contains("std::is_same<::PublicType,", proof);
            string source = Path.Combine(root, "proof.cpp");
            await File.WriteAllTextAsync(source, proof);
            ImmutableArray<string> arguments = ["--no-default-config", "--target=" + target.triple, "-std=c++23", "-fsyntax-only", "-Werror", source,
                .. target.toolchain.arguments,
                .. (target.toolchain.sysRoot is string sysroot ? new[] { "--sysroot=" + sysroot } : []), "-isystem", sdk,
                .. target.toolchain.systemIncludeFolders.Concat(target.toolchain.cxxSystemIncludeFolders).SelectMany(directory => new[] { "-isystem", directory })];
            var compiled = await ProcessExecutor.ExecuteAsync(target.toolchain.compilerPath!, arguments, root, TimeSpan.FromSeconds(60));
            Assert.False(compiled.timedOut);
            Assert.True(compiled.exitCode == 0, compiled.standardOutput + compiled.standardError);
            var drift = await ProcessExecutor.ExecuteAsync(target.toolchain.compilerPath!, arguments.Add("-DBGCS_PRIVATE_ALIAS_FLOAT=1"), root, TimeSpan.FromSeconds(60));
            Assert.False(drift.timedOut);
            Assert.NotEqual(0, drift.exitCode);
            Assert.Contains("alias canonical type differs", drift.standardOutput + drift.standardError);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("support-header", null)]
    [InlineData("BGCS_ALIAS_FLOAT", "alias canonical type")]
    [InlineData("BGCS_GLOBAL_FLOAT", "variable type")]
    [InlineData("BGCS_STATIC_FLOAT", "variable type")]
    [InlineData("BGCS_ENUM_DRIFT", "enumeration value")]
    [InlineData("BGCS_INTEGER_DRIFT", "scalar constant value")]
    [InlineData("BGCS_NAMED_DRIFT", "scalar constant value")]
    [InlineData("BGCS_FLOAT_DRIFT", "scalar constant IEEE bits")]
    [InlineData("BGCS_DOUBLE_DRIFT", "scalar constant IEEE bits")]
    [InlineData("BGCS_NOT_CONSTANT", "constant expression")]
    [InlineData("BGCS_SHADOW_PROOF", "canonical proof token is shadowed")]
    public async Task ActualCppCompilerChecksCanonicalFactsAndFreezesMacrosBeforeSupportHeaders(string? mutation, string? expectedFailure)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-canonical-proof-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "native.hpp"), input = Path.Combine(root, "proof.cpp");
            await File.WriteAllTextAsync(header, Header);
            var target = ToolchainResolver.ResolveToolchain(new TargetResolver().Resolve(new(new("host"))), SourceLanguage.Cpp, "clang");
            NativeModule module = Module(target.targetId.value, target.triple);
            string proof = new NativeCanonicalAssertionEmitter().Emit(module, [header], "#define BGCS_PREAMBLE_VALUE 42", "#define BGCS_POSTAMBLE_VALUE BGCS_PREAMBLE_VALUE");
            Assert.True(proof.IndexOf("constexpr auto", StringComparison.Ordinal) < proof.IndexOf("#include <type_traits>", StringComparison.Ordinal));
            Assert.Contains("std::is_same<::SDK::Count", proof);
            Assert.Contains("std::is_same<::Api", proof);
            Assert.Contains("decltype(::global_value)", proof);
            Assert.Contains("decltype(::Public::value)", proof);
            Assert.DoesNotContain("->bits", proof);
            ImmutableArray<string> extra = [];
            if (mutation == "support-header")
            {
                string support = Path.Combine(root, "support");
                Directory.CreateDirectory(support);
                await File.WriteAllTextAsync(Path.Combine(support, "type_traits"), "#include_next <type_traits>\n#undef BGCS_INTEGER\n#define BGCS_INTEGER 99\n");
                extra = ["-I", support];
                proof += "\n#if BGCS_INTEGER != 99\n#error BGCS support-header control did not execute\n#endif\n";
            }
            else if (mutation is not null) extra = ["-D" + mutation + "=1"];
            await File.WriteAllTextAsync(input, proof);
            ImmutableArray<string> arguments = ["--no-default-config", "--target=" + target.triple, "-std=c++23", "-fsyntax-only", "-Werror", input,
                .. target.toolchain.arguments,
                .. (target.toolchain.sysRoot is string sysroot ? new[] { "--sysroot=" + sysroot } : []),
                .. target.toolchain.systemIncludeFolders.Concat(target.toolchain.cxxSystemIncludeFolders).SelectMany(directory => new[] { "-isystem", directory }), .. extra];
            var result = await ProcessExecutor.ExecuteAsync(target.toolchain.compilerPath!, arguments, root, TimeSpan.FromSeconds(60));
            Assert.False(result.timedOut);
            string diagnostics = result.standardOutput + result.standardError;
            if (expectedFailure is null) Assert.True(result.exitCode == 0, diagnostics);
            else
            {
                Assert.NotEqual(0, result.exitCode);
                Assert.Contains(expectedFailure, diagnostics, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("bitfield")]
    [InlineData("special-scalar")]
    [InlineData("alias-cycle")]
    [InlineData("owner-cycle")]
    [InlineData("source-injection")]
    public void UnsupportedOrMalformedCanonicalFactsCannotProduceAProof(string failure)
    {
        NativeModule original = Module("proof", "x86_64-unknown-linux-gnu");
        NativeType[] types = original.types.ToArray();
        NativeDeclaration[] declarations = original.declarations.ToArray();
        if (failure == "special-scalar") types[0] = types[0] with { primitiveKind = "Float16", spelling = "System.Half", size = 2 };
        if (failure == "alias-cycle")
        {
            int index = Array.FindIndex(types, type => type.id.value == "api");
            types[index] = types[index] with { elementType = types[index].id };
        }
        if (failure == "bitfield")
        {
            int index = Array.FindIndex(declarations, declaration => declaration.id.value == "static-value");
            declarations[index] = declarations[index] with { isStatic = false, role = string.Empty, bitWidth = 1 };
        }
        if (failure == "owner-cycle")
        {
            int index = Array.FindIndex(declarations, declaration => declaration.id.value == "api-decl");
            declarations[index] = declarations[index] with { parent = declarations[index].id };
        }
        if (failure == "source-injection")
        {
            int index = Array.FindIndex(declarations, declaration => declaration.id.value == "api-decl");
            declarations[index] = declarations[index] with { qualifiedName = "Api; static_assert(true);" };
        }
        NativeModule changed = new(original.targetId, original.triple, original.pointerSize, types.ToImmutableArray(), declarations.ToImmutableArray(), []) { language = SourceLanguage.Cpp };
        if (failure is "alias-cycle" or "owner-cycle") Assert.Throws<InvalidOperationException>(() => new NativeCanonicalAssertionEmitter().Emit(changed, ["native.hpp"]));
        else Assert.Throws<NotSupportedException>(() => new NativeCanonicalAssertionEmitter().Emit(changed, ["native.hpp"]));
    }

    private static NativeModule RecursiveModule(
        string target,
        string triple,
        bool recordFirst
    ) {
        StableId node = new("node"), pointer = new("node-pointer"), alias = new("node-alias"), chain = new("node-alias-chain"), qualified = new("node-qualified");
        StableId constAlias = new("node-const-alias"), callback = new("node-callback"), callbackPointer = new("node-callback-pointer"), callable = new("node-callable"), empty = new("void");
        StableId record = new("node-decl"), next = new("node-next"), notify = new("node-notify");
        SourceLocation location = new("recursive.hpp", 1, 1);
        ImmutableArray<NativeType> types =
        [
            new(node, NativeTypeCategory.Record, "Node", 16, 8) { declaration = record },
            new(pointer, NativeTypeCategory.Pointer, "Node*", 8, 8) { elementType = node },
            new(alias, NativeTypeCategory.Alias, "NodePtr", 8, 8) { elementType = pointer, declaration = new("node-alias-decl") },
            new(chain, NativeTypeCategory.Alias, "NodePtrAlias", 8, 8) { elementType = alias, declaration = new("node-chain-decl") },
            new(qualified, NativeTypeCategory.Qualified, "Node* const", 8, 8) { elementType = pointer, qualifier = "Const" },
            new(constAlias, NativeTypeCategory.Alias, "NodeConstPointer", 8, 8) { elementType = qualified, declaration = new("node-const-decl") },
            new(empty, NativeTypeCategory.Primitive, "void", 0, null) { primitiveKind = "Void" },
            new(callable, NativeTypeCategory.Function, "void(Node*)", null, null) { returnType = empty, parameterTypes = [chain], callingConvention = "C" },
            new(callbackPointer, NativeTypeCategory.Pointer, "void(*)(Node*)", 8, 8) { elementType = callable },
            new(callback, NativeTypeCategory.Alias, "NodeCallback", 8, 8) { elementType = callbackPointer, declaration = new("node-callback-decl") }
        ];
        NativeDeclaration owner = new(record, "Class", "Node", "Node", node, location)
            { role = "Struct", isCompleteDefinition = true, children = [next, notify] };
        ImmutableArray<NativeDeclaration> aliases =
        [
            new(new("node-alias-decl"), "Typedef", "NodePtr", "NodePtr", alias, location),
            new(new("node-chain-decl"), "Typedef", "NodePtrAlias", "NodePtrAlias", chain, location),
            new(new("node-const-decl"), "Typedef", "NodeConstPointer", "NodeConstPointer", constAlias, location),
            new(new("node-callback-decl"), "Typedef", "NodeCallback", "NodeCallback", callback, location)
        ];
        ImmutableArray<NativeDeclaration> declarations = recordFirst ? ImmutableArray.Create(owner).AddRange(aliases) : aliases.Add(owner);
        declarations = declarations
            .Add(new(next, "Field", "next", "Node::next", chain, location) { parent = record, visibility = "Public" })
            .Add(new(notify, "Field", "notify", "Node::notify", callback, location) { parent = record, visibility = "Public" });
        return new(target, triple, 8, types, declarations, []) { language = SourceLanguage.Cpp };
    }

    private static NativeModule Module(string target, string triple)
    {
        StableId integer = new("int"), unsigned = new("unsigned"), count = new("count"), api = new("api"), choice = new("choice"), publicType = new("public-type"), hidden = new("hidden"), pointer = new("hidden-pointer"), constInteger = new("const-int");
        StableId countDeclaration = new("count-decl"), apiDeclaration = new("api-decl"), choiceDeclaration = new("choice-decl"), item = new("item"), publicDeclaration = new("public-decl"), staticValue = new("static-value"), hiddenDeclaration = new("hidden-decl"), bits = new("bits");
        StableId functionType = new("function-type"), functionPointer = new("function-pointer"), callback = new("callback"), callbackDeclaration = new("callback-decl"), empty = new("void");
        SourceLocation source = new("native.hpp", 1, 1);
        ImmutableArray<NativeType> types =
        [
            new(integer, NativeTypeCategory.Primitive, "int", 4, 4) { primitiveKind = "Int", isSigned = true },
            new(constInteger, NativeTypeCategory.Qualified, "const int", 4, 4) { qualifier = "Const", elementType = integer },
            new(unsigned, NativeTypeCategory.Primitive, "unsigned int", 4, 4) { primitiveKind = "UnsignedInt", isSigned = false },
            new(count, NativeTypeCategory.Alias, "SDK::Count", 4, 4) { elementType = integer, declaration = countDeclaration },
            new(api, NativeTypeCategory.Alias, "Api", 4, 4) { elementType = count, declaration = apiDeclaration },
            new(choice, NativeTypeCategory.Enumeration, "Choice", 4, 4) { elementType = integer, declaration = choiceDeclaration },
            new(publicType, NativeTypeCategory.Record, "Public", 1, 1) { declaration = publicDeclaration },
            new(hidden, NativeTypeCategory.Record, "SDK::Hidden", 4, 4) { declaration = hiddenDeclaration },
            new(pointer, NativeTypeCategory.Pointer, "SDK::Hidden*", 8, 8) { elementType = hidden },
            new(empty, NativeTypeCategory.Primitive, "void", 0, null) { primitiveKind = "Void" },
            new(functionType, NativeTypeCategory.Function, "void(int)", null, null) { returnType = empty, parameterTypes = [integer], callingConvention = "C" },
            new(functionPointer, NativeTypeCategory.Pointer, "void(*)(int)", 8, 8) { elementType = functionType },
            new(callback, NativeTypeCategory.Alias, "Callback", 8, 8) { elementType = functionPointer, declaration = callbackDeclaration }
        ];
        ImmutableArray<NativeDeclaration> declarations =
        [
            new(countDeclaration, "Typedef", "Count", "SDK::Count", count, source) { isSystemDeclaration = true },
            new(apiDeclaration, "Typedef", "Api", "Api", api, source),
            new(callbackDeclaration, "Typedef", "Callback", "Callback", callback, source),
            new(choiceDeclaration, "Enum", "Choice", "Choice", choice, source) { children = [item], isCompleteDefinition = true },
            new(item, "EnumItem", "Only", "Choice::Only", null, source) { parent = choiceDeclaration, evaluatedIntegralValue = "7" },
            new(publicDeclaration, "Class", "Public", "Public", publicType, source) { children = [staticValue], isCompleteDefinition = true, role = "Struct" },
            new(staticValue, "Field", "value", "Public::value", api, source) { parent = publicDeclaration, role = "Variable", visibility = "Public", isStatic = true },
            new(new("global"), "Field", "global_value", "global_value", api, source) { role = "Variable" },
            new(new("named-constant"), "Field", "named_constant", "named_constant", constInteger, source)
                { role = "Variable", constantValue = new(NativeScalarConstantKind.Integer, "42", 4, true, new(EvidenceLevel.Declared, "fixture")) },
            new(hiddenDeclaration, "Class", "Hidden", "SDK::Hidden", hidden, source) { children = [bits], isCompleteDefinition = true, isSystemDeclaration = true },
            new(bits, "Field", "bits", "SDK::Hidden::bits", unsigned, source) { parent = hiddenDeclaration, visibility = "Private", bitWidth = 1, isSystemDeclaration = true },
            new(new("read-hidden"), "Function", "read_hidden", "read_hidden", null, source) { returnType = pointer, role = "Function", callingConvention = "C" },
            new(new("read-count"), "Function", "read_count", "read_count", null, source) { returnType = count, role = "Function", callingConvention = "C" },
            Macro("BGCS_INTEGER", new(NativeScalarConstantKind.Integer, "42", 4, true, new(EvidenceLevel.Declared, "fixture"))),
            Macro("BGCS_BOOLEAN", new(NativeScalarConstantKind.Boolean, "1", 1, null, new(EvidenceLevel.Declared, "fixture"))),
            Macro("BGCS_FLOAT", new(NativeScalarConstantKind.Binary32, "00000000", 4, null, new(EvidenceLevel.Declared, "fixture"))),
            Macro("BGCS_DOUBLE", new(NativeScalarConstantKind.Binary64, "8000000000000000", 8, null, new(EvidenceLevel.Declared, "fixture"))),
            Macro("BGCS_ENUM_VALUE", new(NativeScalarConstantKind.Integer, "7", 4, true, new(EvidenceLevel.Declared, "fixture")))
        ];
        return new(target, triple, 8, types, declarations, []) { language = SourceLanguage.Cpp };

        NativeDeclaration Macro(string name, NativeScalarConstant value) => new(new("macro:" + name), "Macro", name, name, null, source)
            { constantValue = value, macro = new(false, [], true) };
    }

    private static string WorkerRoot()
    {
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "BindGen-CS.slnx"))) repository = repository.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(repository!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }

    private const string Header = """
        namespace SDK {
        #if defined(BGCS_ALIAS_FLOAT)
        using Count = float;
        #else
        using Count = int;
        #endif
        class Hidden { unsigned int bits:1; };
        }
        using Api = SDK::Count;
        using Callback = void(*)(int);
        #if defined(BGCS_GLOBAL_FLOAT)
        extern float global_value;
        #else
        extern Api global_value;
        #endif
        struct Public {
        #if defined(BGCS_STATIC_FLOAT)
            static float value;
        #else
            static Api value;
        #endif
        };
        enum class Choice:int {
        #if defined(BGCS_ENUM_DRIFT)
            Only = 8
        #else
            Only = 7
        #endif
        };
        SDK::Hidden* read_hidden();
        SDK::Count read_count();
        #if defined(BGCS_NAMED_DRIFT)
        inline constexpr int named_constant = 43;
        #else
        inline constexpr int named_constant = 42;
        #endif
        #if defined(BGCS_NOT_CONSTANT)
        int runtime_constant();
        #define BGCS_INTEGER runtime_constant()
        #elif defined(BGCS_INTEGER_DRIFT)
        #define BGCS_INTEGER 43
        #else
        #define BGCS_INTEGER BGCS_POSTAMBLE_VALUE
        #endif
        #define BGCS_BOOLEAN true
        #if defined(BGCS_FLOAT_DRIFT)
        #define BGCS_FLOAT (-0.0f)
        #else
        #define BGCS_FLOAT (+0.0f)
        #endif
        #if defined(BGCS_DOUBLE_DRIFT)
        #define BGCS_DOUBLE (+0.0)
        #else
        #define BGCS_DOUBLE (-0.0)
        #endif
        #define BGCS_ENUM_VALUE Choice::Only
        #if defined(BGCS_SHADOW_PROOF)
        #define is_same malicious_type_trait
        #endif
        """;
}
