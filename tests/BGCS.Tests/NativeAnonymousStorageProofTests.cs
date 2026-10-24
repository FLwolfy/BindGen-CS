using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Inputs;
using BGCS.Core;
using BGCS.Configuration;
using BGCS.Core.Native;
using BGCS.Core.Interop;
using BGCS.Frontend.Clang.Workers;
using BGCS.Infrastructure.Execution;
using BGCS.NativeBridge.Emission;
using BGCS.Targets.Desktop.Toolchains;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeAnonymousStorageProofTests
{
    [Fact]
    public async Task CppAnonymousContainerQualifiersRemainPartOfTheFrozenProof()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs anonymous cv " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "native.h");
            await File.WriteAllTextAsync(header, """
                struct Envelope {
                    virtual ~Envelope() {}
                #if BGCS_CHANGE
                    volatile
                #else
                    const
                #endif
                    union { int value; float real; } member = {};
                };
                Envelope* make();
                """);
            await using var host = TestHostPaths.CreateHost();
            string worker = Path.Combine(RepositoryRoot(), "extensions/frontends/BGCS.Frontend.Clang.Worker/bin/Release/net10.0/BGCS.Frontend.Clang.Worker.dll");
            NativeModule module = (await new FrontendWorkerClient(worker, host).ParseAsync(new(NativeConsumerHarness.ParserTarget(SourceLanguage.Cpp), SourceLanguage.Cpp, [header])
            { pathBase = root, parseMacros = false }, CancellationToken.None)).module;
            Assert.DoesNotContain(module.diagnostics, diagnostic => diagnostic.isFailure);
            string proof = Path.Combine(root, "proof.cpp");
            await File.WriteAllTextAsync(proof, new NativeStorageAssertionEmitter().Emit(module, ["native.h"], NativeAssertionConventions.For(module)));
            foreach (int change in new[] { 0, 1 })
            {
                var compiled = await ProcessExecutor.ExecuteAsync(NativeConsumerHarness.FindCompiler(true),
                    ["-std=c++23", "-fsyntax-only", "-DBGCS_CHANGE=" + change, proof, .. NativeConsumerHarness.CompilerSdkArguments()], root, TimeSpan.FromSeconds(45));
                Assert.False(compiled.timedOut);
                if (change == 0) Assert.True(compiled.exitCode == 0, compiled.standardOutput + compiled.standardError);
                else
                {
                    Assert.NotEqual(0, compiled.exitCode);
                    Assert.Contains("BGCS native structural const qualifier", compiled.standardOutput + compiled.standardError);
                }
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(SourceLanguage.C)]
    [InlineData(SourceLanguage.Cpp)]
    public async Task SourceNamesRespectTagAliasAndQualifiedScopes(SourceLanguage language)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs scoped storage " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "native.h");
            await File.WriteAllTextAsync(header, language == SourceLanguage.C ? """
                struct Value { int tag; };
                typedef int Value;
                typedef struct { int number; } Anonymous;
                typedef Anonymous Alias;
                typedef struct { struct Value tagged; Value scalar; Alias nested; } Packet;
                Packet echo(Packet value);
                """ : """
                namespace Left { typedef struct { struct Nested { int detail; }; int number; Nested nested; } Value; typedef Value Alias; }
                namespace Right { struct Value { float number; }; }
                typedef struct { enum { Member = 7 }; Left::Alias left; Right::Value right; } Packet;
                Packet echo(Packet value);
                """);
            await using var host = TestHostPaths.CreateHost();
            string worker = Path.Combine(RepositoryRoot(), "extensions/frontends/BGCS.Frontend.Clang.Worker/bin/Release/net10.0/BGCS.Frontend.Clang.Worker.dll");
            NativeModule module = (await new FrontendWorkerClient(worker, host).ParseAsync(new(NativeConsumerHarness.ParserTarget(language), language, [header])
            { pathBase = root, parseMacros = false }, CancellationToken.None)).module;
            Assert.DoesNotContain(module.diagnostics, diagnostic => diagnostic.isFailure);
            if (language == SourceLanguage.Cpp)
                Assert.Contains(module.declarations, declaration => declaration.kind == "EnumItem" && declaration.qualifiedName == "Packet::Member");
            string proof = new NativeStorageAssertionEmitter().Emit(module, ["native.h"], NativeAssertionConventions.For(module));
            Assert.DoesNotContain("(unnamed", proof);
            string source = Path.Combine(root, language == SourceLanguage.C ? "proof.c" : "proof.cpp");
            await File.WriteAllTextAsync(source, proof);
            var compiled = await ProcessExecutor.ExecuteAsync(NativeConsumerHarness.FindCompiler(language == SourceLanguage.Cpp),
                [language == SourceLanguage.C ? "-std=c17" : "-std=c++23", "-fsyntax-only", source, .. NativeConsumerHarness.CompilerSdkArguments(language)], root, TimeSpan.FromSeconds(45));
            Assert.False(compiled.timedOut);
            Assert.True(compiled.exitCode == 0, compiled.standardOutput + compiled.standardError);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task AnonymousDtoArgumentsResultsAndCallbacksInvokeWithoutNamingTheirStorage(
        BindingImportMode mode,
        bool sourceRuntime
    ) {
        string root = Path.Combine(Path.GetTempPath(), "bgcs anonymous calls " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string fixture = Path.Combine(RepositoryRoot(), "tests/wasm/TypedBridge");
            File.Copy(Path.Combine(fixture, "native.h"), Path.Combine(root, "native.h"));
            File.Copy(Path.Combine(fixture, "native.c"), Path.Combine(root, "native.c"));
            BindingConfiguration configuration = new()
            {
                schemaVersion = 2,
                library = new() { id = "anonymous_calls", nativeName = "anonymous_calls" },
                source = new() { language = SourceLanguage.C, headers = ["native.h"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
                api = new() { @namespace = "Generated.AnonymousCalls", name = "Api", importMode = mode },
                semantics = new()
                {
                    callbackParameters = [new()
                    {
                        function = "Invoke", parameter = "callback", lifetime = NativeLifetimeKind.Call, retained = false,
                        threading = NativeCallbackThreading.CallerThread, userDataParameter = "state", callableUserDataIndex = 1
                    }]
                },
                runtime = new() { mode = sourceRuntime ? "source" : "package" },
                output = new() { layout = sourceRuntime ? "singleFile" : "split" },
                cache = new() { enabled = false }
            };
            string worker = Path.Combine(RepositoryRoot(), "extensions/frontends/BGCS.Frontend.Clang.Worker/bin/Release/net10.0");
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(worker).Build();
            GenerationResult generated = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, root));
            Assert.True(generated.success, generated.GetDiagnosticSummary());
            string target = Assert.Single(Directory.GetDirectories(generated.outputRoot));
            string factory = mode == BindingImportMode.FunctionTable ? "Api.Load(binary)" : "Api.Create()";
            string probe = $$"""
                using System;
                using System.Runtime.CompilerServices;
                using System.Runtime.InteropServices;
                using Generated.AnonymousCalls;
                public static unsafe class AnonymousProbe {
                    public static int Run(string binary) {
                        using var api = {{factory}};
                        Sample input = new() { first = 11, scale = 1.25, second = 31 }, result = default;
                        input.storage.marker = 41;
                        api.Raw.Echo(&input, &result);
                        if(result.first != 16 || result.scale != 2.5 || result.second != 28 || result.storage.marker != 42) return 1;
                        int state = 7;
                        api.Raw.Invoke(&Transform, &input, &state, &result);
                        if(result.first != 18 || result.scale != 3.75 || result.second != 24 || result.storage.marker != 48) return 2;
                        return 42;
                    }
                    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
                    private static void Transform(Sample* input, void* state, Sample* result) {
                        result->first = input->first + *(int*)state;
                        result->scale = input->scale * 3;
                        result->second = input->second - *(int*)state;
                        result->storage.marker = input->storage.marker + *(int*)state;
                    }
                }
                """;
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(target, "managed"),
                Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(target))), "anonymous_calls", probe, "AnonymousProbe", root, sourceRuntime));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(SourceLanguage.C, false)]
    [InlineData(SourceLanguage.C, true)]
    [InlineData(SourceLanguage.Cpp, false)]
    [InlineData(SourceLanguage.Cpp, true)]
    public Task AnonymousMembersUseStructuralProofsWithRealLeafIdentity(
        SourceLanguage language,
        bool system
    )
        => Verify(language, system, 0);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public Task StructuralProofRejectsEqualSizeTypeExtentQualifierOffsetAndPackingDrift(int change)
        => Verify(SourceLanguage.C, true, change);

    private static async Task Verify(
        SourceLanguage language,
        bool system,
        int change
    ) {
        string root = Path.Combine(Path.GetTempPath(), "bgcs anonymous storage 來源 " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "native.h");
            await File.WriteAllTextAsync(Path.Combine(root, "storage.h"), (system ? "#pragma clang system_header\n" : string.Empty) + """
                #if BGCS_CHANGE == 1
                typedef float Word;
                #else
                typedef int Word;
                #endif
                #if BGCS_CHANGE == 2
                #define BGCS_COUNT 3
                #else
                #define BGCS_COUNT 2
                #endif
                #if BGCS_CHANGE == 3
                #define BGCS_QUALIFIER const
                #else
                #define BGCS_QUALIFIER volatile
                #endif
                #if BGCS_CHANGE == 5
                #pragma pack(push, 1)
                #endif
                typedef struct {
                    char prefix;
                    union {
                        Word values[BGCS_COUNT];
                        BGCS_QUALIFIER int flags[2];
                        void* slots[4];
                        struct {
                #if BGCS_CHANGE == 4
                            int second; int first;
                #else
                            int first; int second;
                #endif
                            int 測量;
                        } pair;
                        struct {
                #if BGCS_CHANGE == 6
                            unsigned char code; int count;
                #else
                            int count; unsigned char code;
                #endif
                        } rows[2];
                    } storage;
                    const struct { int value; int* pointer; } immutable;
                    volatile struct { unsigned count; } signal;
                    int tail;
                } Packet;
                #if BGCS_CHANGE == 5
                #pragma pack(pop)
                #endif
                """);
            await File.WriteAllTextAsync(header, "#include \"storage.h\"\nPacket echo(Packet value);\n");
            var target = NativeConsumerHarness.ParserTarget(language);
            await using var host = TestHostPaths.CreateHost();
            string worker = Path.Combine(RepositoryRoot(), "extensions/frontends/BGCS.Frontend.Clang.Worker/bin/Release/net10.0/BGCS.Frontend.Clang.Worker.dll");
            NativeModule module = (await new FrontendWorkerClient(worker, host).ParseAsync(new(target, language, [header])
            { pathBase = root, parseMacros = false }, CancellationToken.None)).module;
            Assert.DoesNotContain(module.diagnostics, diagnostic => diagnostic.isFailure);
            string proof = new NativeStorageAssertionEmitter().Emit(module, ["native.h"], NativeAssertionConventions.For(module));
            Assert.Contains("native structural field type differs", proof);
            Assert.Contains("storage.pair.first", proof);
            Assert.Contains("storage.pair.測量", proof);
            Assert.Contains("storage.rows[0].count", proof);
            Assert.DoesNotContain("(unnamed", proof);
            Assert.DoesNotContain("(anonymous", proof);
            Assert.DoesNotContain("typedef typeof(", proof);
            string source = Path.Combine(root, language == SourceLanguage.C ? "proof.c" : "proof.cpp");
            await File.WriteAllTextAsync(source, proof);
            ImmutableArray<string> arguments = [language == SourceLanguage.C ? "-std=c17" : "-std=c++23", "-fsyntax-only", source,
                "-DBGCS_CHANGE=" + change, .. NativeConsumerHarness.CompilerSdkArguments(language)];
            var compiled = await ProcessExecutor.ExecuteAsync(NativeConsumerHarness.FindCompiler(language == SourceLanguage.Cpp), arguments, root, TimeSpan.FromSeconds(45));
            Assert.False(compiled.timedOut);
            string diagnostics = compiled.standardOutput + compiled.standardError;
            if (change == 0) Assert.True(compiled.exitCode == 0, diagnostics);
            else
            {
                Assert.NotEqual(0, compiled.exitCode);
                Assert.Contains("BGCS native", diagnostics);
            }
            if (OperatingSystem.IsWindows() && language == SourceLanguage.C)
            {
                string compiler = MsvcToolchainDiscovery.FindCompiler("x64") ?? throw new FileNotFoundException("The C17 assertion regression requires MSVC.");
                MsvcToolchainInfo sdk = MsvcToolchainDiscovery.Resolve(compiler, "x64");
                var native = await ProcessExecutor.ExecuteAsync(compiler, ["/nologo", "/std:c17", "/Zs", "/TC", "/utf-8", "/DBGCS_CHANGE=" + change,
                    .. sdk.includeDirectories.Select(directory => "/I" + directory), source], root, TimeSpan.FromSeconds(45));
                Assert.False(native.timedOut);
                if (change == 0) Assert.True(native.exitCode == 0, native.standardOutput + native.standardError);
                else Assert.NotEqual(0, native.exitCode);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "BindGen-CS.slnx"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("The fixture repository is missing.");
    }
}
