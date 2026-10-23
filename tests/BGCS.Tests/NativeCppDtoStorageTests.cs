using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core.Bridge;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeCppDtoStorageTests
{
    [Theory]
    [InlineData("package")]
    [InlineData("source")]
    public async Task CompilerProvenDtos_PreserveNestedAndArrayFieldsThroughPointerCopyChannels(string runtimeMode)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-cpp-dto-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "dto.hpp"), Header);
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            GenerationResult result = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(Configuration(runtimeMode), root));
            Assert.True(result.success, result.GetDiagnosticSummary());
            string target = Assert.Single(Directory.GetDirectories(result.outputRoot));
            NativeBridgePlan plan = JsonSerializer.Deserialize<NativeBridgePlan>(await File.ReadAllTextAsync(Path.Combine(target, "native", "metadata", "bridge.module.json")))!;
            foreach (string name in new[] { "Inner", "Packet", "Standalone", "Aligned", "View" })
            {
                var original = plan.source.declarations.Single(declaration => declaration.kind == "Class" && declaration.qualifiedName == "Demo::" + name);
                Assert.True(original.isStandardLayout);
                Assert.True(original.isTriviallyCopyable);
                Assert.Contains(plan.types, type => type.kind == BridgeTypeKind.Record && type.nativeType == original.type);
                Assert.DoesNotContain(plan.exports, export => export.operation.ownerType == original.type && export.operation.kind is BridgeOperationKind.Construct or BridgeOperationKind.DefaultConstruct or BridgeOperationKind.Destroy);
            }
            var packet = plan.types.Single(type => type.kind == BridgeTypeKind.Record && plan.source.GetType(type.nativeType!.Value).spelling == "Demo::Packet");
            Assert.Equal(new long[] { 0, 4, 12 }, packet.recordStorage!.fields.Select(field => field.offset));
            string binary = Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(target)));
            Invoke(Path.Combine(target, "managed"), binary, runtimeMode);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task DtoCopyInCxx17_FailsBeforePublishingUnsupportedGeneratedSource()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-cpp-dto-cxx17-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "dto.hpp"), Header);
            BindingConfiguration configuration = Configuration("package");
            configuration = configuration with { source = configuration.source with { languageStandard = "c++17" } };
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            GenerationResult result = await host.GenerateAsync(new(configuration, root));
            Assert.False(result.success);
            Assert.Contains("bridge.dto-copy", result.GetDiagnosticSummary());
            Assert.Contains("C++20", result.GetDiagnosticSummary());
            Assert.False(Directory.Exists(Path.Combine(root, "Generated")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }


    private static (NativeDeclaration Field, NativeType FieldType, NativeType RecordType) CountFacts(NativeModule module)
    {
        NativeDeclaration record = Assert.Single(module.declarations, declaration => declaration.kind == "Class" && declaration.qualifiedName == "Demo::Inner");
        NativeDeclaration field = Assert.Single(record.children.Select(module.GetDeclaration), declaration => declaration.kind == "Field" && declaration.name == "count");
        NativeType type = module.GetType(field.type!.Value);
        while (type.category == NativeTypeCategory.Alias) type = module.GetType(type.elementType!.Value);
        return (field, type, module.GetType(record.type!.Value));
    }

    private static BindingConfiguration Configuration(string runtime) => new()
    {
        schemaVersion = 2, library = new() { id = "dto", nativeName = "dto_fixture" },
        source = new() { language = SourceLanguage.Cpp, headers = ["dto.hpp"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
        api = new() { @namespace = "Generated.NativeDto", name = "DtoApi", names = ImmutableDictionary<string, string>.Empty
            .Add("Demo::Inner", "Inner").Add("Demo::Packet", "Packet").Add("Demo::Standalone", "Standalone").Add("Demo::Aligned", "Aligned").Add("Demo::View", "View") },
        runtime = new() { mode = runtime },  cache = new() { enabled = false }
    };

    private static void Invoke(string sources, string binary, string runtimeMode)
    {
        string consumer = """
            using Generated.NativeDto;
            public static unsafe class DtoConsumer {
                public static int Run(string binary) {
                    using var api = DtoApi.Load(binary);
                    int payload = 42;
                    View view = default, echoed = default;
                    view.data = &payload; view.context = &payload; view.length = 1;
                    if (api.Raw.EchoView(&view, &echoed, null) != 0 || echoed.data != &payload || echoed.context != &payload || echoed.length != 1) return 11;
                    if (*(int*)echoed.data != 42) return 12;
                    if (sizeof(Packet) != 24 || sizeof(Inner) != 8 || sizeof(Standalone) != 4 || sizeof(Aligned) != 32) return 1;
                    if (global::System.Runtime.InteropServices.Marshal.OffsetOf<Packet>("inner").ToInt64() != 4) return 2;
                    Packet* input = (Packet*)global::System.Runtime.InteropServices.NativeMemory.AlignedAlloc(32, 32);
                    Packet* output = (Packet*)global::System.Runtime.InteropServices.NativeMemory.AlignedAlloc(32, 32);
                    Aligned* alignedInput = (Aligned*)global::System.Runtime.InteropServices.NativeMemory.AlignedAlloc(32, 32);
                    Aligned* alignedOutput = (Aligned*)global::System.Runtime.InteropServices.NativeMemory.AlignedAlloc(64, 32);
                    try {
                        *input = default; *output = default; *alignedInput = default; *alignedOutput = default;
                        input->prefix = 2; input->inner.count = 5; input->inner.active = 1;
                        input->inner.flags[0] = 1; input->inner.flags[1] = 0;
                        input->values[0] = 10; input->values[1] = 20; input->values[2] = 30;
                        int expectedCalls = checked(api.Calls() + 1);
                        if (api.Raw.Transform(input, output, null) != 0) return 3;
                        if (output->prefix != 3 || output->inner.count != 7 || output->inner.active != 0 || output->values[2] != 35) return 4;
                        if (input->prefix != 2 || input->inner.count != 5 || input->inner.active != 1 || input->values[2] != 30) return 5;
                        if (api.Calls() != expectedCalls) return 6;
                        input->inner.active = 255;
                        if (api.Raw.Transform(input, output, null) == 0 || api.Calls() != expectedCalls || output->inner.count != 0) return 7;
                        input->inner.active = 1; input->inner.flags[1] = 255;
                        if (api.Raw.Transform(input, output, null) == 0 || api.Calls() != expectedCalls || output->inner.count != 0) return 8;
                        input->inner.flags[1] = 0;
                        if (api.Raw.Transform((Packet*)((byte*)input + 1), output, null) == 0 || api.Calls() != expectedCalls || output->inner.count != 0) return 9;
                        alignedInput->number = 41;
                        if (api.Raw.TransformAligned(alignedInput, alignedOutput, null) != 0 || alignedOutput->number != 42) return 10;
                        if (api.Raw.TransformAligned(alignedInput, (Aligned*)((byte*)alignedOutput + 1), null) == 0) return 11;
                        return 42;
                    }
                    finally {
                        global::System.Runtime.InteropServices.NativeMemory.AlignedFree(input);
                        global::System.Runtime.InteropServices.NativeMemory.AlignedFree(output);
                        global::System.Runtime.InteropServices.NativeMemory.AlignedFree(alignedInput);
                        global::System.Runtime.InteropServices.NativeMemory.AlignedFree(alignedOutput);
                    }
                }
            }
            """;
        string[] references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        CSharpCompilation compilation = CSharpCompilation.Create("NativeDto_" + Guid.NewGuid().ToString("N"),
            Directory.EnumerateFiles(sources, "*.cs", SearchOption.AllDirectories).Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), new(LanguageVersion.CSharp14), path))
                .Append(CSharpSyntaxTree.ParseText(consumer, new(LanguageVersion.CSharp14))),
            references.Concat(runtimeMode == "package" ? [typeof(BGCS.Runtime.NativeModuleSession).Assembly.Location] : Array.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path)),
            new(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error));
        using MemoryStream image = new();
        var emitted = compilation.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        image.Position = 0;
        AssemblyLoadContext load = new("BGCS.NativeDto.Test", isCollectible: true);
        nint pinnedModule = 0;
        try
        {
            Assembly assembly = load.LoadFromStream(image);
            // Keep the module and its counter alive across both complete runs.
            // This proves nonzero initial state even on loaders that unload a
            // module immediately after the generated context releases it.
            pinnedModule = System.Runtime.InteropServices.NativeLibrary.Load(binary);
            MethodInfo run = assembly.GetType("DtoConsumer", true)!.GetMethod("Run")!;
            Assert.Equal(42, run.Invoke(null, [binary]));
            Assert.Equal(42, run.Invoke(null, [binary]));
        }
        finally
        {
            if (pinnedModule != 0) System.Runtime.InteropServices.NativeLibrary.Free(pinnedModule);
            load.Unload();
        }
    }

    private const string Header = """
        #include <stdint.h>
        #include <stddef.h>
        namespace Demo {
        #ifdef BGCS_DTO_FIELD_FLOAT
        using Count = float;
        #else
        using Count = int;
        #endif
        #ifdef BGCS_DTO_FIELD_REORDERED
        struct Inner { bool active; bool flags[2]; Count count; };
        #else
        struct Inner { Count count; bool active; bool flags[2]; };
        #endif
        struct Packet { uint8_t prefix; Inner inner; int values[3]; };
        struct Standalone { int number; };
        struct View { void* data; const void* context; int length; };
        struct alignas(32) Aligned { int number; };
        inline int calls = 0;
        inline int Calls() { return calls; }
        inline Packet Transform(Packet value) { ++calls; ++value.prefix; value.inner.count += 2; value.inner.active = !value.inner.active; value.values[2] += 5; return value; }
        inline Aligned TransformAligned(Aligned value) { ++value.number; return value; }
        inline View EchoView(View value) { return value; }
        }
        // Reproduce the Windows UCRT macro even on other compiler hosts.
        // This expression is not constexpr under Clang; DTO proofs must use
        // the compiler's independent offset primitive instead.
        #undef offsetof
        #define offsetof(s,m) ((::size_t)&reinterpret_cast<char const volatile&>((((s*)0)->m)))
        """;

    private static string WorkerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(directory!.FullName, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
