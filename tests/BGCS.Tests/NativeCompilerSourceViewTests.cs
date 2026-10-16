using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Collections.Immutable;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts.Inputs;
using BGCS.Contracts;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;
using System.Threading;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeCompilerSourceViewTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "WindowsNativeProvider")]
    public async Task AbsoluteAndDynamicMissingHeaderObservationsAreReplayedBeforePublication(bool dynamic)
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Path.Combine(Path.GetTempPath(), "bgcs MSVC observed source " + Guid.NewGuid().ToString("N"));
        string external = Path.Combine(Path.GetTempPath(), "bgcs MSVC absolute query " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(external);
        try
        {
            string optional = Path.Combine(external, "optional.h");
            string spelling = optional.Replace('\\', '/');
            await File.WriteAllTextAsync(Path.Combine(root, "api.h"),
                (dynamic ? "#define QUERY_HEADER \"" + spelling + "\"\n" : string.Empty) +
                "#if __has_include(" + (dynamic ? "QUERY_HEADER" : "\"" + spelling + "\"") + ")\n" +
                "struct Selected { float value; };\n#else\nstruct Selected { int value; };\n#endif\n");
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "query", nativeName = "query" },
                source = new() { language = SourceLanguage.C, headers = ["api.h"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
                nativeBuild = new() { mode = "none", provider = "msvc" }, cache = new() { enabled = true }
            };
            await using (BindingHost initial = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build())
            {
                GenerationResult first = await initial.GenerateAsync(new(configuration, root));
                Assert.True(first.success, first.GetDiagnosticSummary());
            }
            string output = Path.Combine(root, "Generated");
            var previous = Directory.GetFiles(output, "*", SearchOption.AllDirectories).ToImmutableDictionary(path => Path.GetRelativePath(output, path),
                path => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
            await using BindingHost mutating = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot())
                .AddValidator("create-observed-header", 0, () => new CreatingValidator(optional)).Build();
            GenerationResult changed = await mutating.GenerateAsync(new(configuration, root));
            Assert.False(changed.success);
            Assert.Contains("Native compiler source-view observations changed", changed.GetDiagnosticSummary());
            var current = Directory.GetFiles(output, "*", SearchOption.AllDirectories).ToImmutableDictionary(path => Path.GetRelativePath(output, path),
                path => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
            Assert.Equal(previous.OrderBy(pair => pair.Key), current.OrderBy(pair => pair.Key));
        }
        finally { Directory.Delete(root, recursive: true); Directory.Delete(external, recursive: true); }
    }

    private sealed class CreatingValidator(string optional) : IGenerationValidator
    {
        public string id => "create-observed-header";
        public int priority => 0;
        public ValueTask<ImmutableArray<BindingDiagnostic>> ValidateAsync(ValidationRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.WriteAllText(optional, "/* appeared outside every configured header search root */");
            return ValueTask.FromResult(ImmutableArray<BindingDiagnostic>.Empty);
        }
    }

    [Fact]
    [Trait("Category", "WindowsNativeProvider")]
    public async Task MsvcInitialViewSelectsItsOwnHelpersAndRejectsBuildOnlyDefinitionDrift()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Path.Combine(Path.GetTempPath(), "bgcs MSVC original source view " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "api.h"), """
                #if defined(__clang__)
                #define CLANG_ONLY_VALUE 3
                static inline int clang_only_helper(void) { return 3; }
                #else
                #define ACTUAL_NATIVE_VALUE 18446744073709551615ULL
                /** The original native-only helper documentation. */
                #if defined(BGCS_BUILD_ONLY_DRIFT)
                static inline float native_helper(void) { return 42.0f; }
                #else
                static inline int native_helper(void) { return 42; }
                #endif
                #endif
                """);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "native_view", nativeName = "native_view" },
                source = new() { language = SourceLanguage.C, headers = ["api.h"], includeTransitivelyReferencedHeaders = false, includeHeaderHelpers = true },
                api = new() { @namespace = "Generated.NativeView", name = "NativeApi",
                    names = ImmutableDictionary<string, string>.Empty.Add("ACTUAL_NATIVE_VALUE", "ActualNativeValue") },
                runtime = new() { mode = "source" }, cache = new() { enabled = true },
                nativeBuild = new() { mode = "sources", provider = "msvc" }
            };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            BuildResult built = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(built.success, built.GetDiagnosticSummary());
            string target = Assert.Single(Directory.GetDirectories(built.outputRoot));
            string managed = Path.Combine(target, "managed");
            string binary = Assert.Single(Directory.GetFiles(Path.Combine(target, "native", "bin"), "*.dll"));
            Assert.DoesNotContain(Directory.GetFiles(managed, "*.cs", SearchOption.AllDirectories), path => File.ReadAllText(path).Contains("ClangOnly", StringComparison.Ordinal));
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(managed, binary, "native_view", """
                using Generated.NativeView;
                public static class SourceViewProbe {
                    public static int Run(string binary) {
                        using var api = NativeApi.Load(binary);
                        if (!NativeApi.Constants.ActualNativeValue.Equals(ulong.MaxValue)) return 1;
                        return api.NativeHelper();
                    }
                }
                """, "SourceViewProbe", root, sourceRuntime: true));
            var previous = Directory.GetFiles(built.outputRoot, "*", SearchOption.AllDirectories).ToImmutableDictionary(path => Path.GetRelativePath(built.outputRoot, path),
                path => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
            BuildResult drift = await host.BuildAsync(new(new(configuration with { nativeBuild = configuration.nativeBuild with { compilerArguments = ["/DBGCS_BUILD_ONLY_DRIFT=1"] } }, root)));
            Assert.False(drift.success);
            Assert.True(drift.GetDiagnosticSummary().Contains("BGCS native callable signature differs from parsed facts", StringComparison.Ordinal), drift.GetDiagnosticSummary());
            var current = Directory.GetFiles(built.outputRoot, "*", SearchOption.AllDirectories).ToImmutableDictionary(path => Path.GetRelativePath(built.outputRoot, path),
                path => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
            Assert.Equal(previous.OrderBy(pair => pair.Key), current.OrderBy(pair => pair.Key));
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
