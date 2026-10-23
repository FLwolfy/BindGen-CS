using System;
using System.IO;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core.Inputs;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeCTypedefProofTests
{
    [Fact]
    public async Task AnonymousTypedefAndDistinctNamedTagRetainTheirRealCProofSpellings()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs C typedef proof " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "native.h"), """
                #if defined(_WIN32)
                #define API __declspec(dllexport)
                #else
                #define API __attribute__((visibility("default")))
                #endif
                typedef struct { int value; } AnonymousStorage;
                typedef struct RealTag { AnonymousStorage nested; } PublicStorage;
                typedef enum { Zero=0, One=1 } PublicEnum;
                typedef enum NegativeTag { Minus=-1, Plus=1 } NegativeEnum;
                API int enum_echo(PublicEnum value, NegativeEnum negative);
                API void storage_set(PublicStorage* output, int value);
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "native.c"), """
                #include "native.h"
                API int enum_echo(PublicEnum value, NegativeEnum negative) { return (int)value + (int)negative; }
                API void storage_set(PublicStorage* output, int value) { output->nested.value = value; }
                """);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 2, library = new() { id = "typedef_proof", nativeName = "typedef_proof" },
                source = new() { language = SourceLanguage.C, headers = ["native.h"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
                cache = new() { enabled = false }};
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            GenerationResult result = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, root), compileManaged: true);
            Assert.True(result.success, result.GetDiagnosticSummary());
            string assertions = new BGCS.NativeBridge.Emission.NativeStorageAssertionEmitter().Emit(
                Assert.Single(result.targetModels).native, [Path.Combine(root, "native.h")],
                Assert.Single(result.targetModels).resolvedTarget!.sourceConventions!, string.Empty, string.Empty);
            string proof = Path.Combine(root, "typedef-assertions.c");
            await File.WriteAllTextAsync(proof, assertions);
            await NativeConsumerHarness.RunAsync(NativeConsumerHarness.FindCompiler(false),
                ["-std=c17", "-fsyntax-only", proof, .. NativeConsumerHarness.CompilerSdkArguments(SourceLanguage.C)], root);
            Assert.Contains("sizeof(AnonymousStorage)", assertions);
            Assert.DoesNotContain("sizeof(struct AnonymousStorage)", assertions);
            Assert.Contains("sizeof(struct RealTag)", assertions);
            Assert.DoesNotContain("struct PublicStorage", assertions);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string WorkerRoot()
    {
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "BindGen-CS.slnx"))) repository = repository.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(repository!.FullName, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
