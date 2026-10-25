using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core;
using BGCS.Core.Inputs;
using BGCS.Core.Targets;
using BGCS.Extension.BrowserWasm;
using Xunit;

namespace BGCS.Tests;

public sealed class BrowserGenerationRegressionTests
{
    [Fact]
    public async Task UnsupportedFunctionTableFailsWithAlternativesAndPreservesPublishedOutput()
    {
        using Fixture fixture = new();
        await using BindingHost host = Builder().Build();
        BindingConfiguration configuration = fixture.Configuration();
        GenerationResult supported = await host.GenerateAsync(new(configuration, fixture.root));
        Assert.True(supported.success, supported.GetDiagnosticSummary());
        string[] previous = Bytes(supported);
        GenerationResult rejected = await host.GenerateAsync(new(configuration with
        { api = configuration.api with { importMode = BindingImportMode.FunctionTable } }, fixture.root));
        Assert.False(rejected.success);
        string diagnostics = rejected.GetDiagnosticSummary();
        Assert.Contains("FunctionTable", diagnostics, StringComparison.Ordinal);
        Assert.Contains("LibraryImport", diagnostics, StringComparison.Ordinal);
        Assert.Contains("DllImport", diagnostics, StringComparison.Ordinal);
        Assert.Equal(previous, Bytes(supported));
    }

    [Theory]
    [InlineData("compiler")]
    [InlineData("sdkMetadata")]
    [InlineData("header")]
    [InlineData("membership")]
    [InlineData("searchOrder")]
    [InlineData("sysroot")]
    [InlineData("defines")]
    [InlineData("arguments")]
    [InlineData("language")]
    [InlineData("missingCompiler")]
    public async Task OfficialBrowserTargetReusesOnlyUnchangedCompleteInputs(string change)
    {
        using Fixture fixture = new();
        BindingConfiguration configuration = fixture.Configuration();
        if (change == "missingCompiler") File.Delete(fixture.compiler);
        await using BindingHost host = Builder().Build();
        await AssertStage("miss");
        await AssertStage("hit");
        switch (change)
        {
            case "compiler":
            case "missingCompiler":
                await File.WriteAllTextAsync(fixture.compiler, "changed installation identity");
                break;
            case "sdkMetadata":
                await File.WriteAllTextAsync(fixture.version, "3.1.57");
                break;
            case "header":
                await File.WriteAllTextAsync(fixture.header, "typedef float Selected;\n");
                break;
            case "membership":
                await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(fixture.header)!, "new.h"), "/* new candidate */\n");
                break;
            case "searchOrder":
                configuration = configuration with
                {
                    source = configuration.source with
                    { systemIncludeDirectories = configuration.source.systemIncludeDirectories.Reverse().ToImmutableArray() }
                };
                break;
            case "sysroot":
                string next = Path.Combine(fixture.root, "other-sdk");
                Directory.CreateDirectory(Path.Combine(next, "include"));
                configuration = configuration with
                {
                    targets = [configuration.targets[0] with
                { settings = configuration.targets[0].settings with { sysRoot = next } }]
                };
                break;
            case "defines":
                configuration = configuration with { source = configuration.source with { defines = ["FEATURE=2"] } };
                break;
            case "arguments":
                configuration = configuration with { source = configuration.source with { arguments = ["-funsigned-char"] } };
                break;
            case "language":
                configuration = configuration with { source = configuration.source with { language = SourceLanguage.Cpp, languageStandard = "c++23" } };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change));
        }
        await AssertStage("miss");
        await AssertStage("hit");

        async Task AssertStage(string expected)
        {
            GenerationResult result = await host.GenerateAsync(new(configuration, fixture.root));
            Assert.True(result.success, result.GetDiagnosticSummary());
            var stage = Assert.Single(result.cacheStages.Where(stage => stage.stage == "csharpEmission"));
            Assert.Equal(expected, stage.status);
        }
    }

    [Fact]
    public async Task BrowserCancellationAndConflictingTripleNeverPublish()
    {
        using Fixture fixture = new();
        await using BindingHost host = Builder().Build();
        BindingConfiguration configuration = fixture.Configuration();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.GenerateAsync(new(configuration, fixture.root), new CancellationToken(true)));
        GenerationResult rejected = await host.GenerateAsync(new(configuration with
        { targets = [configuration.targets[0] with { triple = "wasm64-unknown-emscripten" }] }, fixture.root));
        Assert.False(rejected.success);
        Assert.Contains("triple", rejected.GetDiagnosticSummary(), StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(fixture.root, "Generated")));
    }

    private static BindingHostBuilder Builder()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        string worker = Path.Combine(directory!.FullName, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
        return BindingGenerator.CreateBuilder().UseWorkerDirectory(worker).AddPlugin<BrowserWasmPlugin>();
    }

    private static string[] Bytes(GenerationResult result) => result.artifacts.OrderBy(artifact => artifact.relativePath, StringComparer.Ordinal)
        .Select(artifact => artifact.relativePath + ":" + Convert.ToBase64String(File.ReadAllBytes(Path.Combine(result.outputRoot, artifact.relativePath)))).ToArray();

    private sealed class Fixture : IDisposable
    {
        internal string root { get; } = Path.Combine(Path.GetTempPath(), "bgcs-browser-cache-" + Guid.NewGuid().ToString("N"));
        internal string compiler => Path.Combine(root, "sdk", "bin", "clang++");
        internal string version => Path.Combine(root, "sdk", "emscripten", "emscripten-version.txt");
        internal string header => Path.Combine(root, "sdk", "sysroot", "include", "selected.h");

        internal Fixture()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(compiler)!);
            Directory.CreateDirectory(Path.GetDirectoryName(version)!);
            Directory.CreateDirectory(Path.GetDirectoryName(header)!);
            Directory.CreateDirectory(Path.Combine(root, "headers"));
            Directory.CreateDirectory(Path.Combine(root, "alternate"));
            File.WriteAllText(compiler, "read-only installation hint, never executed");
            File.WriteAllText(version, "3.1.56");
            File.WriteAllText(header, "typedef int Selected;\n");
            File.WriteAllText(Path.Combine(root, "headers", "native.h"), "#include <selected.h>\nSelected read_value(Selected value);\n");
        }

        internal BindingConfiguration Configuration() => new()
        {
            schemaVersion = 2,
            library = new() { id = "browser_cache", nativeName = "native" },
            source = new()
            {
                language = SourceLanguage.C,
                headers = ["headers/native.h"],
                parseMacros = false,
                systemIncludeDirectories = [Path.GetDirectoryName(header)!, Path.Combine(root, "alternate")]
            },
            targets = [new() { id = "emscripten-wasm32-emscripten", settings = new()
                { compilerPath = compiler, sysRoot = Path.Combine(root, "sdk", "sysroot") } }],
            api = new()
            {
                @namespace = "Generated.Browser",
                name = "BrowserApi",
                runtime = new ManagedRuntimeId("dotnet.mono-wasm"),
                importMode = BindingImportMode.LibraryImport
            }
        };

        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
