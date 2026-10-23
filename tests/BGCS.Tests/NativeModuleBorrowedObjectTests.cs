using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core;
using BGCS.Core.Interop;
using BGCS.Core.Inputs;
using BGCS.Core.Managed;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeModuleBorrowedObjectTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task ExplicitModuleBorrowedObjectsKeepNativeStorageAndRespectContextLifetime(BindingImportMode mode, bool sourceRuntime)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-module-borrowed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "widget.h"), """
                #if defined(_WIN32)
                #define API __declspec(dllexport)
                #else
                #define API __attribute__((visibility("default")))
                #endif
                typedef struct Widget { int value; } Widget;
                API Widget* widget_get(int missing);
                API int widget_read(const Widget* self);
                API int widget_add(Widget* self, int value);
                API int widget_storage_value(void);
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "widget.c"), """
                #include "widget.h"
                static Widget storage = { 40 };
                API Widget* widget_get(int missing) { return missing ? (Widget*)0 : &storage; }
                API int widget_read(const Widget* self) { return self->value; }
                API int widget_add(Widget* self, int value) { self->value += value; return self->value; }
                API int widget_storage_value(void) { return storage.value; }
                """);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 2, library = new() { id = "module-borrowed", nativeName = "module_borrowed_fixture" },
                source = new() { language = SourceLanguage.C, headers = ["widget.h"], includeTransitivelyReferencedHeaders = false, parseMacros = false },
                api = new()
                {
                    @namespace = "Generated.ModuleBorrowed", name = "ModuleApi", importMode = mode,
                    style = sourceRuntime ? ManagedApiStyle.Static : ManagedApiStyle.Instance,
                    names = ImmutableDictionary<string, string>.Empty.Add("widget_get", "GetStatic").Add("widget_read", "Read")
                        .Add("widget_add", "Add").Add("widget_storage_value", "StorageValue"),
                    memberFunctions = new[] { "widget_read", "widget_add" }.Select(function => new ManagedReceiverRequest(function, "Widget", "self")
                        { objectName = "WidgetView", kind = ManagedReceiverKind.ModuleBorrowed }).ToImmutableArray(),
                    functionAliases = [new("widget_get", "GetAlias"), new("widget_read", "ReadAlias")],
                    functionContainers = [new("Widgets", [new("widget_get", "Get"), new("widget_read", "Read")])],
                    functions = [new("widget_get") { defaults = [new("missing", new(ManagedDefaultValueKind.Integer, "0"))] }]
                },
                semantics = new()
                {
                    borrowedResults = [new() { function = "widget_get", lifetime = NativeLifetimeKind.Module }],
                    borrowedParameters = new[] { "widget_read", "widget_add" }.Select(function => new BorrowedParameterConfiguration
                        { function = function, parameter = "self", lifetime = NativeLifetimeKind.Call, retainsPointer = false }).ToImmutableArray()
                },
                runtime = new() { mode = sourceRuntime ? "source" : "package" },
                output = new() { layout = sourceRuntime ? "singleFile" : "split" },
                 cache = new() { enabled = false }
            };
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            GenerationResult result = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, root));
            Assert.True(result.success, result.GetDiagnosticSummary());
            TargetInspection target = Assert.Single(result.targetModels);
            Assert.Equal(NativeOwnershipKind.Borrowed, target.interop!.functions.Single(function => function.exportName == "widget_get").returnOwnership!.kind);
            string targetRoot = Path.Combine(result.outputRoot, target.targetId);
            string native = Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(targetRoot)));
            string concrete = sourceRuntime ? "ModuleApiContext" : "ModuleApi";
            string create = mode == BindingImportMode.FunctionTable ? "ApiContext.Load(binary)" : "ApiContext.Create()";
            string shared = sourceRuntime ? $$"""
                {{(mode == BindingImportMode.FunctionTable ? "ModuleApi.Load(binary);" : "ModuleApi.Create();")}}
                using (var borrowed = ModuleApi.GetStatic()) { if (borrowed.Read() != 42) return 12; }
                ModuleApi.Dispose();
                """ : "";
            string consumer = $$"""
                using System;
                using Generated.ModuleBorrowed;
                using ApiContext = Generated.ModuleBorrowed.{{concrete}};
                public static class ModuleBorrowedConsumer {
                    public static int Run(string binary) {
                        using var api = {{create}};
                        using var other = {{create}};
                        using var first = api.GetStatic();
                        if (first.Read() != 40 || api.Read(first) != 40) return 1;
                        if (first.Add(2) != 42 || api.StorageValue() != 42) return 2;
                        using (var alias = api.GetAlias(0)) { if (api.ReadAlias(alias) != 42) return 3; }
                        using (var view = api.Widgets.Get(0)) { if (api.Widgets.Read(view) != 42) return 4; }
                        try { other.Read(first); return 5; } catch (ArgumentException) { }
                        try { api.GetStatic(1); return 6; } catch (InvalidOperationException) { }
                        var closed = api.GetStatic(); closed.Dispose(); closed.Dispose();
                        try { closed.Read(); return 7; } catch (ObjectDisposedException) { }
                        if (api.StorageValue() != 42) return 8;
                        api.Dispose();
                        try { first.Read(); return 9; } catch (ObjectDisposedException) { }
                        first.Dispose(); first.Dispose();
                        if (other.StorageValue() != 42) return 10;
                        using (var stillNative = other.GetStatic()) { if (stillNative.Read() != 42) return 11; }
                        {{shared}}
                        return 42;
                    }
                }
                """;
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot, "managed"), native,
                configuration.library.nativeName, consumer, "ModuleBorrowedConsumer", root, sourceRuntime));
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
