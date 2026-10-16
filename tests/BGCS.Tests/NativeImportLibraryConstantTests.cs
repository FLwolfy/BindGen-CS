using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Inputs;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeImportLibraryConstantTests
{
    [Theory]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task ConsumerLibraryConstantsCompileInvokeAndInvalidateWithoutReplacingGoodOutput(BindingImportMode mode, bool sourceRuntime)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-native-import-constant-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "answer.h"), """
                #if defined(_WIN32)
                #define API_EXPORT __declspec(dllexport)
                #else
                #define API_EXPORT __attribute__((visibility("default")))
                #endif
                API_EXPORT int answer(void);
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "answer.c"), "#include \"answer.h\"\nAPI_EXPORT int answer(void){return 42;}\n");
            const string consumer = "namespace Vendor {public static class LibraryNames {public const string Native=\"import_constant_fixture\";} }";
            await File.WriteAllTextAsync(Path.Combine(root, "LibraryNames.cs"), consumer);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "import-constant", nativeName = "import_constant_fixture" },
                source = new() { language = SourceLanguage.C, headers = ["answer.h"], includeTransitivelyReferencedHeaders = false },
                api = new() { @namespace = "Generated.ImportConstant", name = "AnswerApi", importMode = mode,
                    emitLibraryNameConstant = false, importLibraryNameConstant = "Vendor.LibraryNames.Native" },
                managedBuild = new() { sources = ["LibraryNames.cs"] }, nativeBuild = new() { mode = "sources", sources = ["answer.c"] },
                runtime = new() { mode = sourceRuntime ? "source" : "package" },
                output = new() { layout = sourceRuntime ? "singleFile" : "split" }, cache = new() { enabled = true }
            };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            BuildResult built = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(built.success, built.GetDiagnosticSummary());
            var target = Assert.Single(built.generation.targetModels);
            Assert.Equal("Vendor.LibraryNames.Native", target.managed!.importLibraryNameConstant);
            string targetRoot = Path.Combine(built.outputRoot, target.targetId);
            string managed = Path.Combine(targetRoot, "managed");
            string all = string.Join("\n", Directory.GetFiles(managed, "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
            Assert.Contains("global::Vendor.LibraryNames.Native", all, StringComparison.Ordinal);
            Assert.DoesNotContain("public const string NativeLibraryName", all, StringComparison.Ordinal);
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(managed,
                Assert.Single(Directory.GetFiles(Path.Combine(targetRoot, "native", "bin"))), configuration.library.nativeName,
                "using Generated.ImportConstant; public static class LibraryConstantConsumer {public static int Run(string binary){using var api=AnswerApi.Create();return api.Answer();}}\n" + consumer,
                "LibraryConstantConsumer", root, sourceRuntime));
            if (mode == BindingImportMode.DllImport && !sourceRuntime)
            {
                var previous = Directory.GetFiles(built.outputRoot, "*", SearchOption.AllDirectories)
                    .ToDictionary(path => Path.GetRelativePath(built.outputRoot, path), path => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))), StringComparer.Ordinal);
                await File.WriteAllTextAsync(Path.Combine(root, "LibraryNames.cs"), consumer.Replace("import_constant_fixture", "different_native_module", StringComparison.Ordinal));
                GenerationResult rejected = await host.GenerateAsync(new(configuration, root));
                Assert.False(rejected.success);
                Assert.Contains("exactly matches", rejected.GetDiagnosticSummary(), StringComparison.Ordinal);
                var after = Directory.GetFiles(built.outputRoot, "*", SearchOption.AllDirectories)
                    .ToDictionary(path => Path.GetRelativePath(built.outputRoot, path), path => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))), StringComparer.Ordinal);
                Assert.Equal(previous.OrderBy(pair => pair.Key), after.OrderBy(pair => pair.Key));
                await File.WriteAllTextAsync(Path.Combine(root, "LibraryNames.cs"), consumer);
                GenerationResult recovered = await host.GenerateAsync(new(configuration, root));
                Assert.True(recovered.success, recovered.GetDiagnosticSummary());
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void GroupedConfigurationKeepsTheReferenceAndRejectsFunctionTableMisuse()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-import-constant-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string config = Path.Combine(root, "bindgen.json");
            const string json = """
                {"schemaVersion":1,"library":{"id":"answer","nativeName":"native"},"source":{"headers":["answer.h"]},
                "api":{"importMode":"dllImport","emitLibraryNameConstant":false,"importLibraryNameConstant":"Vendor.LibraryNames.Native"}}
                """;
            File.WriteAllText(config, json);
            BindingConfiguration configuration = GenerationRequest.FromFile(config).configuration;
            Assert.Equal("Vendor.LibraryNames.Native", configuration.api.importLibraryNameConstant);
            Assert.False(configuration.api.emitLibraryNameConstant);
            File.WriteAllText(config, json.Replace("dllImport", "functionTable", StringComparison.Ordinal));
            Assert.Throws<ArgumentException>(() => GenerationRequest.FromFile(config));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string WorkerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(directory!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
