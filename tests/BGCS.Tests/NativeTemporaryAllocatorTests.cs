using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core;
using BGCS.Core.Inputs;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeTemporaryAllocatorTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task GeneratedText_UsesIndependentNativePairsAndCleanupAfterContextClosure(BindingImportMode mode, bool sourceRuntime)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-native-temporary-allocator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string repository = RepositoryRoot();
            string header = await File.ReadAllTextAsync(Path.Combine(repository, "examples", "CustomAllocator", "native.h"));
            header += "\n#ifdef __cplusplus\nextern \"C\" {\n#endif\n"
                + "EXAMPLE_EXPORT uint32_t example_text8(const unsigned char* text);\n"
                + "EXAMPLE_EXPORT uint32_t example_text16(const uint16_t* text);\n"
                + "EXAMPLE_EXPORT uint32_t example_text32(const uint32_t* text);\n"
                + "#ifdef __cplusplus\n}\n#endif\n";
            await File.WriteAllTextAsync(Path.Combine(root, "native.h"), header);
            string native = await File.ReadAllTextAsync(Path.Combine(repository, "examples", "CustomAllocator", "native.cpp"));
            native += "\nuint32_t example_text8(const unsigned char* text) { return text[0] == 65 && text[5] == 0 ? text[1] : 0; }\n"
                + "uint32_t example_text16(const uint16_t* text) { return text[0] == 65 && text[3] == 0 ? text[1] : 0; }\n"
                + "uint32_t example_text32(const uint32_t* text) { return text[0] == 65 && text[2] == 0 ? text[1] : 0; }\n";
            await File.WriteAllTextAsync(Path.Combine(root, "native.cpp"), native);
            ImmutableArray<StringParameterConfiguration>.Builder textContracts = ImmutableArray.CreateBuilder<StringParameterConfiguration>();
            foreach ((string name, int width) in new[] { ("example_text8", 1), ("example_text16", 2), ("example_text32", 4) })
                textContracts.Add(new()
                {
                    function = name, parameter = "text", encoding = width switch { 1 => NativeTextEncoding.Utf8, 2 => NativeTextEncoding.Utf16, _ => NativeTextEncoding.Utf32 },
                    codeUnitWidth = width, byteOrder = NativeTextByteOrder.Native, lengthUnit = NativeLengthUnit.CodeUnits,
                    direction = NativeParameterDirection.Input, lifetime = NativeLifetimeKind.Call,
                    retainsPointer = false, zeroTerminated = true, allowsNull = false
                });
            BindingConfiguration configuration = new()
            {
                schemaVersion = 2, library = new() { id = "temporary-allocator", nativeName = "temporary_allocator_fixture" },
                source = new() { language = SourceLanguage.C, headers = ["native.h"], includeTransitivelyReferencedHeaders = false, parseMacros = false },
                api = new() { @namespace = "Generated.TemporaryAllocator", name = "AllocatorApi", importMode = mode,
                    style = sourceRuntime ? ManagedApiStyle.Static : ManagedApiStyle.Instance },
                runtime = new() { mode = sourceRuntime ? "source" : "package" },
                output = new() { layout = sourceRuntime ? "singleFile" : "split" },
                semantics = new() { stringParameters = textContracts.ToImmutable() },
                 cache = new() { enabled = false }
            };
            string build = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(Path.Combine(repository,
                "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", build, "net10.0")).Build();
            GenerationResult result = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, root));
            Assert.True(result.success, result.GetDiagnosticSummary());
            string target = Assert.Single(Directory.GetDirectories(result.outputRoot));
            string binary = Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(target)));
            string runtime = sourceRuntime ? "Generated.TemporaryAllocator.Runtime" : "BGCS.Runtime";
            string context = sourceRuntime ? "AllocatorApiContext" : "AllocatorApi";
            string factory = mode == BindingImportMode.FunctionTable ? "ApiContext.Load(binary, factory)" : "ApiContext.Create(factory)";
            string sharedFactory = mode == BindingImportMode.FunctionTable ? "AllocatorApi.Load(binary, factory)" : "AllocatorApi.Create(factory)";
            string shared = sourceRuntime ? $$"""
                factory = CreateFactory(third, false);
                {{sharedFactory}};
                if (AllocatorApi.ExampleText8("A😀") != 240 || allocations(third) != 1 || releases(third) != 1) return 9;
                AllocatorApi.Dispose();
                """ : "";
            string probe = $$"""
                using System;
                using System.Runtime.InteropServices;
                using Generated.TemporaryAllocator;
                using ApiContext = Generated.TemporaryAllocator.{{context}};
                using Runtime = {{runtime}};
                public static unsafe class TemporaryAllocatorConsumer
                {
                    public static int Run(string binary)
                    {
                        nint module = NativeLibrary.Load(binary);
                        var create = (delegate* unmanaged[Cdecl]<nint>)NativeLibrary.GetExport(module, "example_allocator_create");
                        var destroy = (delegate* unmanaged[Cdecl]<nint, int>)NativeLibrary.GetExport(module, "example_allocator_destroy");
                        var allocate = (delegate* unmanaged[Cdecl]<nint, nuint, nuint, nint>)NativeLibrary.GetExport(module, "example_allocate");
                        var release = (delegate* unmanaged[Cdecl]<nint, nint, nuint, nuint, int>)NativeLibrary.GetExport(module, "example_release");
                        var allocations = (delegate* unmanaged[Cdecl]<nint, ulong>)NativeLibrary.GetExport(module, "example_allocations");
                        var releases = (delegate* unmanaged[Cdecl]<nint, ulong>)NativeLibrary.GetExport(module, "example_releases");
                        nint first = create(), second = create(), third = create();
                        ApiContext? closeOnRelease = null;
                        Runtime.NativeAllocator? captured = null;
                        Runtime.NativeModuleSession? failedSession = null;
                        Func<Runtime.NativeModuleSession, Runtime.NativeAllocator> CreateFactory(nint state, bool capture)
                            => session =>
                            {
                                var allocator = new Runtime.NativeAllocator("native-state-" + state,
                                    (length, alignment) => allocate(state, length, alignment),
                                    (address, length, alignment) =>
                                    {
                                        closeOnRelease?.Dispose();
                                        if (release(state, address, length, alignment) != 0) throw new InvalidOperationException("Native allocation pair mismatch.");
                                    }, Runtime.NativeAllocatorThreadDomain.AnyThread, session);
                                if (capture) captured = allocator;
                                return allocator;
                            };
                        try
                        {
                            Func<Runtime.NativeModuleSession, Runtime.NativeAllocator> factory = CreateFactory(first, true);
                            using var one = {{factory}};
                            factory = CreateFactory(second, false);
                            using var two = {{factory}};
                            if (one.ExampleText8("A😀") != 240 || one.ExampleText16("A😀") != 0xD83D) return 1;
                            closeOnRelease = one;
                            if (one.ExampleText32("A😀") != 0x1F600) return 2;
                            closeOnRelease = null;
                            if (allocations(first) != 3 || releases(first) != 3) return 3;
                            try { captured!.Allocate(8, 8); return 4; } catch (ObjectDisposedException) { }
                            if (two.ExampleText32("A😀") != 0x1F600 || allocations(second) != 1 || releases(second) != 1) return 5;
                            try { two.ExampleText8(null!); return 6; } catch (ArgumentNullException) { }
                            if (allocations(second) != 1) return 7;
                            factory = session => { failedSession = session; return Runtime.NativeAllocator.CreateNativeMemory(); };
                            try { using var invalid = {{factory}}; return 8; } catch (InvalidOperationException) { }
                            try { using var call = failedSession!.AcquireCall(); return 10; } catch (ObjectDisposedException) { }
                            {{shared}}
                            return 42;
                        }
                        finally
                        {
                            if (destroy(first) != 0 || destroy(second) != 0 || destroy(third) != 0)
                                throw new InvalidOperationException("A native allocator still owns temporary text.");
                            NativeLibrary.Free(module);
                        }
                    }
                }
                """;
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(target, "managed"), binary,
                configuration.library.nativeName, probe, "TemporaryAllocatorConsumer", root, sourceRuntime));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("The test repository root is unavailable.");
    }
}
