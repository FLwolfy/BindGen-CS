using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core.Bridge;
using BGCS.Core.Inputs;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeGeneratedClassProviderTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Construct(
        int value,
        out nint result,
        out nint error
    );
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Read(
        nint self,
        out int result,
        out nint error
    );
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Count(
        out int result,
        out nint error
    );
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Destroy(
        nint self,
        out nint error
    );

    [Fact]
    public async Task GeneratedClassSourcesConstructReadAndDestroyThroughExternallyCompiledFixture()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs generated class provider 验收 & " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        nint library = 0;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "counter.hpp"), """
                #pragma once
                namespace Fixture {
                inline int active_instances = 0;
                class Counter {
                    int m_value;
                public:
                    explicit Counter(int value) : m_value(value) { ++active_instances; }
                    ~Counter() noexcept { --active_instances; }
                    int Read() const { return m_value; }
                };
                inline int Count() { return active_instances; }
                }
                """);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 2,
                library = new() { id = "provider_class", nativeName = "provider_class_bridge" },
                source = new() { language = SourceLanguage.Cpp, headers = ["counter.hpp"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
                runtime = new() { mode = "source" },
                cache = new() { enabled = false }
            };
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            GenerationResult generated = await host.GenerateAsync(new(configuration, root));
            Assert.True(generated.success, generated.GetDiagnosticSummary());
            string modelFile = Assert.Single(Directory.GetFiles(generated.outputRoot, "bridge.module.json", SearchOption.AllDirectories));
            NativeBridgePlan model = JsonSerializer.Deserialize<NativeBridgePlan>(await File.ReadAllTextAsync(modelFile))!;
            await NativeConsumerHarness.CompileNativeFixturesAsync(generated, root, configuration.library.nativeName);
            BridgeExport construction = Assert.Single(model.exports, export => export.operation.kind == BridgeOperationKind.Construct
                && model.source.GetType(export.operation.ownerType!.Value).spelling == "Fixture::Counter");
            BridgeExport read = Assert.Single(model.exports, export => export.operation.declaration is { } id
                && model.source.GetDeclaration(id).qualifiedName == "Fixture::Counter::Read");
            BridgeExport count = Assert.Single(model.exports, export => export.operation.declaration is { } id
                && model.source.GetDeclaration(id).qualifiedName == "Fixture::Count");
            BridgeExport destruction = Assert.Single(model.exports, export => export.id == construction.resultRelease);
            library = NativeLibrary.Load(Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(Path.Combine(generated.outputRoot, Assert.Single(generated.targetModels).targetId)))));
            T Function<T>(BridgeExport export) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, export.name));
            Count active = Function<Count>(count);
            Assert.Equal(0, active(out int initialCount, out nint error));
            Assert.Equal(0, initialCount);
            Assert.Equal(0, error);
            Assert.Equal(0, Function<Construct>(construction)(42, out nint instance, out error));
            Assert.NotEqual(0, instance);
            Assert.Equal(0, error);
            try
            {
                Assert.Equal(0, Function<Read>(read)(instance, out int result, out error));
                Assert.Equal(42, result);
                Assert.Equal(0, error);
                Assert.Equal(0, active(out int liveCount, out error));
                Assert.Equal(1, liveCount);
                Assert.Equal(0, error);
            }
            finally
            {
                Assert.Equal(0, Function<Destroy>(destruction)(instance, out error));
                Assert.Equal(0, error);
            }
            Assert.Equal(0, active(out int finalCount, out error));
            Assert.Equal(0, finalCount);
            Assert.Equal(0, error);
        }
        finally
        {
            if (library != 0) NativeLibrary.Free(library);
            Directory.Delete(root, recursive: true);
        }
    }

    private static string WorkerRoot()
    {
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "BindGen-CS.slnx"))) repository = repository.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(repository!.FullName, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
