using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core.Bridge;
using BGCS.Core.Inputs;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeBridgeErrorAllocationTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Scalar(
        out int value,
        out nint error
    );
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Release(nint error);

    [Theory]
    [InlineData("package")]
    [InlineData("source")]
    public async Task ErrorCapture_UsesItsOwnCAllocatorInsteadOfUserGlobalNewDelete(string runtimeMode)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-error-allocation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "allocation.hpp"), """
                namespace Demo { int CheckHooks(); int AllocationCalls(); int DeleteCalls(); int Fail(); }
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "allocation.cpp"), """
                #include "allocation.hpp"
                #include <cstdlib>
                #include <new>
                namespace { int allocations = 0; int deletes = 0; }
                void* operator new(std::size_t size, const std::nothrow_t&) noexcept { ++allocations; return std::malloc(size); }
                void operator delete(void* value) noexcept { ++deletes; std::free(value); }
                void operator delete(void* value, std::size_t) noexcept { ++deletes; std::free(value); }
                namespace Demo {
                int CheckHooks() { void* value = ::operator new(64, std::nothrow); bool allocated = value != nullptr; ::operator delete(value); return allocated ? 1 : 0; }
                int AllocationCalls() { return allocations; }
                int DeleteCalls() { return deletes; }
                int Fail() { throw 7; }
                }
                """);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 2,
                library = new() { id = "allocation", nativeName = "allocation_fixture" },
                source = new() { language = SourceLanguage.Cpp, headers = ["allocation.hpp"], parseMacros = false },
                api = new() { @namespace = "Generated.ErrorAllocation", name = "AllocationApi" },
                runtime = new() { mode = runtimeMode },
                cache = new() { enabled = false }
            };
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            GenerationResult result = await host.GenerateAsync(new(configuration, root));
            Assert.True(result.success, result.GetDiagnosticSummary());
            // ELF replacement operators must bind inside this fixture rather than to the process's C++ runtime.
            // Keep this link policy out of the generator's parser arguments.
            string[] fixtureArguments = OperatingSystem.IsLinux() ? ["-Wl,-Bsymbolic-functions"] : [];
            await NativeConsumerHarness.CompileNativeFixturesAsync(result, root, configuration.library.nativeName,
                arguments: fixtureArguments);
            var abi = Assert.Single(result.targetModels).interop;
            Assert.All(abi.functions.Where(function => function.error is not null), function =>
            {
                Assert.Equal("bridge-error-c-allocator:allocation", function.error!.errorOwnership!.allocator!.Value.value);
                Assert.Equal("any", function.error.errorOwnership.releaseThreadDomain);
            });
            string target = Assert.Single(Directory.GetDirectories(result.outputRoot));
            NativeBridgePlan module = JsonSerializer.Deserialize<NativeBridgePlan>(await File.ReadAllTextAsync(Path.Combine(target, "native", "metadata", "bridge.module.json")))!;
            string binary = Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(target)));
            nint library = NativeLibrary.Load(binary);
            try
            {
                Scalar Function(string name) => Marshal.GetDelegateForFunctionPointer<Scalar>(NativeLibrary.GetExport(library,
                    module.exports.Single(export => export.operation.declaration is { } id && module.source.GetDeclaration(id).name == name).name));
                Scalar allocations = Function("AllocationCalls"), deletes = Function("DeleteCalls");
                Assert.Equal(0, Function("CheckHooks")(out int active, out nint error));
                Assert.Equal(1, active);
                Assert.Equal(0, error);
                Assert.Equal(0, allocations(out int allocationsBefore, out error));
                Assert.Equal(0, deletes(out int deletesBefore, out error));
                Assert.True(allocationsBefore >= 1 && deletesBefore >= 1,
                    $"Replacement hooks are not active in this binary: allocations={allocationsBefore}, deletes={deletesBefore}.");
                Assert.Equal(1, Function("Fail")(out int value, out error));
                Assert.Equal(0, value);
                Assert.NotEqual(0, error);
                Marshal.GetDelegateForFunctionPointer<Release>(NativeLibrary.GetExport(library, module.errorReleaseExport))(error);
                Assert.Equal(0, allocations(out int allocationsAfter, out error));
                Assert.Equal(allocationsBefore, allocationsAfter);
                Assert.Equal(0, deletes(out int deletesAfter, out error));
                Assert.Equal(deletesBefore, deletesAfter);
                InvokeFriendly(Path.Combine(target, "managed"), binary, runtimeMode);
            }
            finally { NativeLibrary.Free(library); }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void InvokeFriendly(
        string sources,
        string binary,
        string runtimeMode
    ) {
        string runtime = runtimeMode == "package" ? "BGCS.Runtime" : "Generated.ErrorAllocation.Runtime";
        string consumer = """
            public static class AllocationConsumer {
                public static int Run(string binary) {
                    using var api = global::Generated.ErrorAllocation.AllocationApi.Load(binary);
                    int allocated = api.AllocationCalls(), deleted = api.DeleteCalls();
                    bool caught = false;
                    try { api.Fail(); } catch (global::RUNTIME.NativeCallException) { caught = true; }
                    return caught && api.AllocationCalls() == allocated && api.DeleteCalls() == deleted ? 42 : 0;
                }
            }
            """.Replace("RUNTIME", runtime, StringComparison.Ordinal);
        string[] references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        CSharpCompilation compilation = CSharpCompilation.Create("ErrorAllocation_" + Guid.NewGuid().ToString("N"),
            Directory.EnumerateFiles(sources, "*.cs", SearchOption.AllDirectories).Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), new(LanguageVersion.CSharp14), path))
                .Append(CSharpSyntaxTree.ParseText(consumer, new(LanguageVersion.CSharp14))),
            references.Concat(runtimeMode == "package" ? [typeof(BGCS.Runtime.NativeModuleSession).Assembly.Location] : Array.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path)),
            new(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error));
        using MemoryStream image = new();
        var emitted = compilation.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        image.Position = 0;
        AssemblyLoadContext load = new("BGCS.ErrorAllocation.Test", isCollectible: true);
        try { Assembly assembly = load.LoadFromStream(image); Assert.Equal(42, assembly.GetType("AllocationConsumer", true)!.GetMethod("Run")!.Invoke(null, [binary])); }
        finally { load.Unload(); }
    }

    private static string WorkerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(directory!.FullName, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
