using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Requests;
using BGCS.Infrastructure.Workers;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeSourceLanguageTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeValue(int value);

    [Theory]
    [InlineData(".c", SourceLanguage.C, "c17")]
    [InlineData(".C", SourceLanguage.Cpp, "c++23")]
    [InlineData(".cpp", SourceLanguage.Cpp, "c++23")]
    public async Task StandaloneManifestPreservesTheSameSourceLanguageAsUnifiedBuild(string extension, SourceLanguage language, string standard)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs native source language 目录 & " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        nint module = 0;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "native.hpp"), "extern \"C\" int native_value(int value);\n");
            string source = Path.Combine(root, "native" + extension);
            string body = language == SourceLanguage.C
                ? "_Static_assert(__STDC_VERSION__ >= 201710L, \"exact C17 mode\");\nint native_value(int value) { return value * 2; }\n"
                : "static_assert(__cplusplus > 202002L, \"exact C++23 mode\");\ntemplate<typename T> T Twice(T value) { return value * 2; }\nextern \"C\" int native_value(int value) { return Twice(value); }\n";
            await File.WriteAllTextAsync(source, "#if defined(_WIN32)\n#define EXPORT __declspec(dllexport)\n#else\n#define EXPORT __attribute__((visibility(\"default\")))\n#endif\n" + body.Replace("int native_value", "EXPORT int native_value", StringComparison.Ordinal));
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "source_language", nativeName = "source_language_bridge" },
                source = new() { language = SourceLanguage.Cpp, headers = ["native.hpp"], parseMacros = false },
                runtime = new() { mode = "source" }, cache = new() { enabled = false },
                nativeBuild = new() { mode = "sources", sources = [Path.GetFileName(source)] }
            };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            BuildResult unified = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(unified.success, unified.GetDiagnosticSummary());
            string manifestPath = Assert.Single(Directory.GetFiles(unified.outputRoot, "bridge.manifest.json", SearchOption.AllDirectories));
            NativeBuildManifest manifest = JsonSerializer.Deserialize<NativeBuildManifest>(await File.ReadAllTextAsync(manifestPath), WorkerProtocol.jsonOptions)!;
            NativeManifestCompilationUnit declared = Assert.Single(manifest.sources, unit => unit.path == source);
            Assert.Equal(language, declared.language);
            Assert.Equal(standard, declared.standard);
            ManifestNativeBuildResult standalone = await host.NativeBuildAsync(new(manifestPath));
            Assert.True(standalone.success, standalone.GetDiagnosticSummary());
            module = NativeLibrary.Load(standalone.nativeArtifact!);
            Assert.Equal(42, Marshal.GetDelegateForFunctionPointer<NativeValue>(NativeLibrary.GetExport(module, "native_value"))(21));
        }
        finally
        {
            if (module != 0) NativeLibrary.Free(module);
            Directory.Delete(root, recursive: true);
        }
    }

    private static string WorkerRoot()
    {
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "BindGen-CS.slnx"))) repository = repository.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(repository!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
