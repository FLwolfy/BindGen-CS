using System;
using System.IO;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts.Inputs;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeCTypedefProofTests
{
    [Theory]
    [InlineData("msvc")]
    [InlineData("clang")]
    [InlineData("clang-cl")]
    [Trait("Category", "WindowsNativeProvider")]
    public async Task AnonymousTypedefAndDistinctNamedTagRetainTheirRealCProofSpellings(string provider)
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Path.Combine(Path.GetTempPath(), "bgcs C typedef proof " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "native.h"), """
                typedef struct { int value; } AnonymousStorage;
                typedef struct RealTag { AnonymousStorage nested; } PublicStorage;
                typedef enum { Zero=0, One=1 } PublicEnum;
                typedef enum NegativeTag { Minus=-1, Plus=1 } NegativeEnum;
                __declspec(dllexport) int enum_echo(PublicEnum value, NegativeEnum negative);
                __declspec(dllexport) void storage_set(PublicStorage* output, int value);
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "native.c"), """
                #include "native.h"
                __declspec(dllexport) int enum_echo(PublicEnum value, NegativeEnum negative) { return (int)value + (int)negative; }
                __declspec(dllexport) void storage_set(PublicStorage* output, int value) { output->nested.value = value; }
                """);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "typedef_proof", nativeName = "typedef_proof" },
                source = new() { language = SourceLanguage.C, headers = ["native.h"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
                cache = new() { enabled = false }, nativeBuild = new() { mode = "sources", provider = provider, sources = ["native.c"] }
            };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            BuildResult result = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(result.success, result.GetDiagnosticSummary());
            string assertions = File.ReadAllText(Path.Combine(result.outputRoot, "windows-x64-msvc", "native", "src", "storage.assertions.c"));
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
        return Path.Combine(repository!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
