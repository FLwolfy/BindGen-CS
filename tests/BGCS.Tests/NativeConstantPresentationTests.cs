using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core;
using BGCS.Core.Inputs;
using BGCS.Core.Managed;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeConstantPresentationTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task ManagedAssertionsAndRealNativeEnumCallsRetainIndependentValues(
        BindingImportMode mode,
        bool sourceRuntime
    ) {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-native-constant-presentation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "values.h"), """
                #if defined(_WIN32)
                #define FIXTURE_EXPORT __declspec(dllexport)
                #else
                #define FIXTURE_EXPORT __attribute__((visibility("default")))
                #endif
                #define NATIVE_VALUE 42
                #define NATIVE_TEXT u8"original"
                typedef enum NativeMode { NATIVE_MODE_FIRST = 7, NATIVE_MODE_SECOND = 9 } NativeMode;
                FIXTURE_EXPORT int native_original(void);
                FIXTURE_EXPORT NativeMode native_mode(NativeMode value);
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "values.c"), """
                #include "values.h"
                FIXTURE_EXPORT int native_original(void) { return NATIVE_VALUE; }
                FIXTURE_EXPORT NativeMode native_mode(NativeMode value) {
                    return value == NATIVE_MODE_FIRST ? NATIVE_MODE_SECOND : NATIVE_MODE_FIRST;
                }
                """);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 2,
                library = new() { id = "presentation", nativeName = "presentation_fixture" },
                source = new() { language = SourceLanguage.C, headers = ["values.h"], includeTransitivelyReferencedHeaders = false },
                api = new()
                {
                    @namespace = "Generated.Presentation",
                    name = "PresentationApi",
                    importMode = mode,
                    style = sourceRuntime ? ManagedApiStyle.Static : ManagedApiStyle.Instance,
                    constants = [new("NATIVE_VALUE") { name = "PresentedValue", managedType = "long", expression = "6L * 7L + 1L", documentation = "Managed value <asserted> && separate." },
                        new("NATIVE_TEXT") { name = "PresentedText", value = new(ManagedDefaultValueKind.String, "managed😀") }],
                    nativeEnums = [new("NativeMode") { name = "Mode", documentation = "Managed enum presentation.",
                        values = [new("NATIVE_MODE_FIRST") { name = "First", value = new(ManagedDefaultValueKind.Integer, "11"), documentation = "Explicit managed value." },
                            new("NATIVE_MODE_SECOND") { name = "Second" }] }]
                },
                runtime = new() { mode = sourceRuntime ? "source" : "package" },
                output = new() { layout = sourceRuntime ? "singleFile" : "split" },

                cache = new() { enabled = false }
            };
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            GenerationResult result = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, root));
            Assert.True(result.success, result.GetDiagnosticSummary());
            var target = Assert.Single(result.targetModels);
            Assert.Equal("42", Assert.Single(target.interop.constants.Where(item => item.name == "NATIVE_VALUE")).value.value);
            var nativeEnum = Assert.Single(target.interop.types.Where(type => type.enumValues.Any(value => value.name == "NATIVE_MODE_FIRST")));
            Assert.Equal("7", nativeEnum.enumValues.Single(value => value.name == "NATIVE_MODE_FIRST").evaluatedValue);
            Assert.Equal(4L, nativeEnum.size);
            Assert.Equal(BGCS.Core.Common.EvidenceLevel.UserAsserted,
                target.managed!.constants.Single(item => item.name == "PresentedValue").presentation!.evidence.level);
            string targetRoot = Path.Combine(result.outputRoot, target.targetId);
            using JsonDocument receipt = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(targetRoot, "metadata", "abi.manifest.json")));
            JsonElement metadata = receipt.RootElement;
            JsonElement nativeValue = metadata.GetProperty("module").GetProperty("constants").EnumerateArray().Single(item => item.GetProperty("name").GetString() == "NATIVE_VALUE");
            Assert.Equal("42", nativeValue.GetProperty("value").GetProperty("value").GetString());
            JsonElement managedValue = metadata.GetProperty("managedConstants").EnumerateArray().Single(item => item.GetProperty("name").GetString() == "PresentedValue");
            Assert.Equal("43L", managedValue.GetProperty("expression").GetString());
            Assert.Equal((int)BGCS.Core.Common.EvidenceLevel.UserAsserted, managedValue.GetProperty("presentationEvidence").GetProperty("level").GetInt32());
            JsonElement presentedEnum = metadata.GetProperty("managedNativeEnumPresentation").EnumerateArray().Single(item => item.GetProperty("name").GetString() == "Mode");
            JsonElement first = presentedEnum.GetProperty("enumValues").EnumerateArray().Single(item => item.GetProperty("name").GetString() == "First");
            string underlying = presentedEnum.GetProperty("enumUnderlyingType").GetString()!;
            Assert.True(underlying is "int" or "uint");
            Assert.Equal(underlying == "uint" ? "11U" : "11", first.GetProperty("value").GetString());
            Assert.Equal((int)BGCS.Core.Common.EvidenceLevel.UserAsserted, first.GetProperty("presentationEvidence").GetProperty("level").GetInt32());
            Assert.True(metadata.GetProperty("managedLibrary").GetProperty("emitLibraryNameConstant").GetBoolean());
            Assert.Equal(configuration.library.nativeName, metadata.GetProperty("managedLibrary").GetProperty("nativeName").GetString());
            string binary = Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(targetRoot)));
            string context = sourceRuntime ? "PresentationApiContext" : "PresentationApi";
            string factory = mode == BindingImportMode.FunctionTable ? "ApiContext.Load(binary)" : "ApiContext.Create()";
            string shared = sourceRuntime ? "if (Different(PresentationApi.NativeLibraryName, PresentationApiContext.NativeLibraryName) || Different(PresentationApi.Constants.PresentedValue, 43L)) return 7;" : "";
            string consumer = $$"""
                using Generated.Presentation;
                using ApiContext = Generated.Presentation.{{context}};
                public static class PresentationConsumer {
                    private static bool Different<T>(T left, T right) => !global::System.Collections.Generic.EqualityComparer<T>.Default.Equals(left, right);
                    public static int Run(string binary) {
                        if (Different(ApiContext.NativeLibraryName, "presentation_fixture")) return 1;
                        if (Different(ApiContext.Constants.PresentedValue, 43L) || Different(ApiContext.Constants.PresentedText, "managed😀")) return 2;
                        if (Different((int)Mode.First, 11) || Different((int)Mode.Second, 9)) return 3;
                        using var api = {{factory}};
                        if (api.Raw.NativeOriginal() != 42 || api.NativeOriginal() != 42) return 4;
                        if ((int)api.Raw.NativeMode((Mode)7) != 9 || (int)api.NativeMode((Mode)9) != 7) return 5;
                        if ((int)api.NativeMode(Mode.First) != 7) return 6;
                        {{shared}}
                        return 42;
                    }
                }
                """;
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot, "managed"), binary,
                configuration.library.nativeName, consumer, "PresentationConsumer", root, sourceRuntime));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string WorkerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(directory!.FullName, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
