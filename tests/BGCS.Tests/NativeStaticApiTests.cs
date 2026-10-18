using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Managed;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeStaticApiTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task StaticFacadeUsesTheSameNativeOwnershipTextAndBufferPlans(BindingImportMode mode, bool sourceRuntime)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-shared-api-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "widget.hpp"), """
                class Widget {
                    inline static int s_live = 0;
                    int m_value;
                public:
                    explicit Widget(int value) : m_value(value) { ++s_live; }
                    ~Widget() noexcept { --s_live; }
                    int Read() const { return m_value; }
                    static int Live() { return s_live; }
                };
                inline int First(const char* text) { return static_cast<unsigned char>(text[0]); }
                inline int Sum(const int* values, unsigned int length) {
                    int sum = 0; for (unsigned int i = 0; i < length; ++i) sum += values[i]; return sum;
                }
                """);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "shared", nativeName = "shared_fixture" },
                source = new() { language = SourceLanguage.Cpp, headers = ["widget.hpp"], parseMacros = false },
                api = new() { @namespace = "Generated.Shared", name = "WidgetApi", style = ManagedApiStyle.Static, importMode = mode,
                    names = ImmutableDictionary<string, string>.Empty.Add("Widget::Widget", "CreateWidget") },
                semantics = new()
                {
                    ownedResults = [new() { function = "Widget::Widget", releaseThreadDomain = "any" }],
                    stringParameters = [new() { function = "First", parameter = "text", encoding = NativeTextEncoding.Utf8, codeUnitWidth = 1,
                        lengthUnit = NativeLengthUnit.CodeUnits, direction = NativeParameterDirection.Input, lifetime = NativeLifetimeKind.Call,
                        zeroTerminated = true, retainsPointer = false, allowsNull = false }],
                    bufferParameters = [new() { function = "Sum", parameter = "values", lengthParameter = "length", direction = NativeParameterDirection.Input,
                        lengthUnit = NativeLengthUnit.Elements, lifetime = NativeLifetimeKind.Call, retainsPointer = false, allowsNull = true }]
                },
                runtime = new() { mode = sourceRuntime ? "source" : "package" },
                output = new() { layout = sourceRuntime ? "singleFile" : "split" },
                 cache = new() { enabled = false }
            };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            GenerationResult result = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, root));
            Assert.True(result.success, result.GetDiagnosticSummary());
            string target = Assert.Single(Directory.GetDirectories(result.outputRoot));
            string binary = Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(target)));
            string initialize = mode == BindingImportMode.FunctionTable ? "WidgetApi.Load(binary);" : "WidgetApi.Create();";
            string independent = mode == BindingImportMode.FunctionTable ? "WidgetApiContext.Load(binary)" : "WidgetApiContext.Create()";
            string consumer = $$"""
                using System;
                using Generated.Shared;
                public static class SharedConsumer {
                    public static int Run(string binary) {
                        if (WidgetApi.IsInitialized) return 1;
                        try { WidgetApi.Live(); return 2; } catch (InvalidOperationException) { }
                        {{initialize}}
                        using var observer = {{independent}};
                        using var owned = WidgetApi.CreateWidget(42);
                        if (WidgetApi.Live() != 1 || owned.Read() != 42 || observer.Live() != 1) return 3;
                        if (WidgetApi.First("ABC") != 65 || WidgetApi.Sum(new int[] { 20, 22 }) != 42) return 4;
                        var previousRaw = WidgetApi.Raw;
                        {{initialize}}
                        if (ReferenceEquals(previousRaw, WidgetApi.Raw)) return 5;
                        try { owned.Read(); return 6; } catch (ObjectDisposedException) { }
                        owned.Dispose();
                        if (owned.releaseFailure is not null || observer.Live() != 0 || WidgetApi.Live() != 0) return 7;
                        using var final = WidgetApi.CreateWidget(40);
                        var closing = WidgetApi.DisposeAsync();
                        if (closing.IsCompleted || WidgetApi.IsInitialized) return 8;
                        final.Dispose();
                        closing.GetAwaiter().GetResult();
                        WidgetApi.Dispose();
                        if (observer.Live() != 0 || final.releaseFailure is not null) return 9;
                        try { WidgetApi.Live(); return 10; } catch (InvalidOperationException) { }
                        return 42;
                    }
                }
                """;
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(target, "managed"), binary,
                configuration.library.nativeName, consumer, "SharedConsumer", root, sourceRuntime));
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
