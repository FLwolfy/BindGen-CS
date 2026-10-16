using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts.Inputs;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeScalarInputProofTests
{
    [Theory]
    [InlineData(SourceLanguage.C, "integer-macro", "clang")]
    [InlineData(SourceLanguage.Cpp, "integer-macro", "clang")]
    [InlineData(SourceLanguage.C, "negative-zero-macro", "clang")]
    [InlineData(SourceLanguage.Cpp, "negative-zero-macro", "clang")]
    [InlineData(SourceLanguage.C, "variable", "clang")]
    [InlineData(SourceLanguage.Cpp, "variable", "clang")]
    [InlineData(SourceLanguage.C, "type-macro", "clang")]
    [InlineData(SourceLanguage.C, "integer-macro", "msvc")]
    [InlineData(SourceLanguage.C, "negative-zero-macro", "msvc")]
    [InlineData(SourceLanguage.C, "variable", "msvc")]
    [InlineData(SourceLanguage.C, "type-macro", "msvc")]
    public async Task NativeMacroFlagsCannotPublishStaleScalarConstants(SourceLanguage language, string scenario, string provider)
    {
        if (provider == "msvc" && !OperatingSystem.IsWindows()) return;
        string root = Path.Combine(Path.GetTempPath(), "bgcs native scalar proof " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string first = scenario == "negative-zero-macro" ? "(-0.0f)" : scenario == "type-macro" ? "42" : scenario == "variable" ? "18446744073709551614ULL" : "18446744073709551615ULL";
            string changed = scenario == "negative-zero-macro" ? "(0.0f)" : scenario == "type-macro" ? "42.0f" : "9223372036854775825ULL";
            string Constant(string value) => scenario == "variable" ? "static const unsigned long long PUBLIC_VALUE = " + value + ";" : "#define PUBLIC_VALUE " + value;
            await File.WriteAllTextAsync(Path.Combine(root, "values.h"), "#if defined(_WIN32)\n#define EXPORT __declspec(dllexport)\n#else\n#define EXPORT __attribute__((visibility(\"default\")))\n#endif\n"
                + "#if defined(BGCS_NATIVE_FACT_CHANGE)\n" + Constant(changed)
                + "\n#else\n" + Constant(first) + "\n#endif\nEXPORT int native_read(void);\n");
            string extension = language == SourceLanguage.C ? "c" : "cpp";
            await File.WriteAllTextAsync(Path.Combine(root, "values." + extension), """
                #include "values.h"
                #if defined(_WIN32)
                #define EXPORT __declspec(dllexport)
                #else
                #define EXPORT __attribute__((visibility("default")))
                #endif
                EXPORT int native_read(void) { return 42; }
                """);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "scalar_proof", nativeName = "scalar_proof" },
                source = new() { language = language, headers = ["values.h"], parseMacros = true, includeTransitivelyReferencedHeaders = false },
                runtime = new() { mode = "source" }, cache = new() { enabled = false },
                nativeBuild = new() { mode = "sources", provider = provider, sources = ["values." + extension] }
            };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            BuildResult initial = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(initial.success, initial.GetDiagnosticSummary());
            Dictionary<string, string> previous = Snapshot(initial.outputRoot);
            BindingConfiguration nativeChanged = configuration with { nativeBuild = configuration.nativeBuild with { compilerArguments = [(provider == "msvc" ? "/D" : "-D") + "BGCS_NATIVE_FACT_CHANGE=1"] } };
            BuildResult rejected = await host.BuildAsync(new(new(nativeChanged, root)));
            Assert.False(rejected.success);
            Assert.Contains("PUBLIC_VALUE (scalar constant value, representation or availability)", rejected.GetDiagnosticSummary());
            Dictionary<string, string> current = Snapshot(initial.outputRoot);
            Assert.Equal(previous.OrderBy(pair => pair.Key), current.OrderBy(pair => pair.Key));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static Dictionary<string, string> Snapshot(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(root, path), path => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))), StringComparer.Ordinal);

    private static string WorkerRoot()
    {
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "BindGen-CS.slnx"))) repository = repository.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(repository!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
