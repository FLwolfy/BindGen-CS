using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core.Bridge;
using BGCS.Core.Inputs;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeCppSequenceTests
{
    [Theory]
    [InlineData("package")]
    [InlineData("source")]
    public async Task TypedSequences_PreserveRawExports_CopyValuesAndPinBorrowedCalls(string runtimeMode)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-cpp-sequences-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "sequences.hpp"), Header);
            BindingConfiguration configuration = Configuration(runtimeMode);
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            GenerationResult result = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, root));
            Assert.True(result.success, result.GetDiagnosticSummary());
            string target = Assert.Single(Directory.GetDirectories(result.outputRoot));
            string binary = Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(target)));
            NativeBridgePlan model = JsonSerializer.Deserialize<NativeBridgePlan>(await File.ReadAllTextAsync(Path.Combine(target, "native", "metadata", "bridge.module.json")))!;
            BridgeExport[] adapters = model.exports.Where(export => export.inputAdapterOf is not null).ToArray();
            Assert.Equal(10, adapters.Length);
            foreach (BridgeExport adapter in adapters) Assert.Contains(model.exports, export => export.id == adapter.inputAdapterOf);
            NativeDeclaration echo = Assert.Single(model.source.declarations.Where(declaration => declaration.qualifiedName == "Demo::Echo8"));
            NativeType codeUnit = model.source.GetType(echo.returnType!.Value);
            Assert.Equal(NativeTypeCategory.Primitive, codeUnit.category);
            Assert.Equal("Char8", codeUnit.primitiveKind);
            Assert.Equal("char8_t", codeUnit.spelling);
            Assert.Equal(1, codeUnit.size);
            Assert.Equal(1, codeUnit.alignment);
            Assert.False(codeUnit.isSigned);
            Invoke(Path.Combine(target, "managed"), binary, runtimeMode);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("Demo::SumSpan", false, "nonretention")]
    [InlineData("Demo::SumRef", false, "nonretention")]
    [InlineData("Demo::Escape", true, "escape")]
    [InlineData("Demo::SumUtf8Span", true, "identical native C element types")]
    public async Task UnsafeSequenceAdapters_FailBeforeCandidatePublication(string function, bool assertCall, string reason)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-cpp-sequence-refusal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "sequences.hpp"), Header);
            BindingConfiguration configuration = Configuration("package");
            configuration = configuration with { bridge = configuration.bridge with { cpp = configuration.bridge.cpp with { sequenceInputAdapters = [Request(function, assertCall)] } } };
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            GenerationResult result = await host.BridgeAsync(new(configuration, root));
            Assert.False(result.success);
            string summary = result.GetDiagnosticSummary();
            Assert.Contains(reason == "nonretention" ? "retainsArgument=false" : reason, summary, StringComparison.Ordinal);
            Assert.False(Directory.Exists(result.outputRoot));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static BindingConfiguration Configuration(string runtimeMode) => new()
    {
        schemaVersion = 2, library = new() { id = "sequences", nativeName = "sequence_fixture" },
        source = new() { language = SourceLanguage.Cpp, headers = ["sequences.hpp"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
        api = new() { @namespace = "Generated.NativeSequences", name = "SequenceApi" },
        semantics = new() { ownedResults = [new() { function = "Demo::Receiver::Receiver(int)", releaseThreadDomain = "any" }] },
        bridge = new() { cpp = new() { sequenceInputAdapters = [Request("Demo::SumVector"), Request("Demo::SumArray"), Request("Demo::SumString"), Request("Demo::SumUtf8"),
            Request("Demo::SumBool"), Request("Demo::SumSpan", true), Request("Demo::SumConstSpan", true), Request("Demo::SumFixedSpan", true),
            Request("Demo::SumRef", true), Request("Demo::Receiver::Sum")] } },
        runtime = new() { mode = runtimeMode },  cache = new() { enabled = false }
    };

    private static SequenceInputAdapterRequest Request(string function, bool call = false) => new(function,
        [new("values") { retainsArgument = call ? false : null, lifetime = call ? NativeLifetimeKind.Call : NativeLifetimeKind.Unknown }]);

    private static void Invoke(string sources, string binary, string runtimeMode)
    {
        string[] references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        string runtime = runtimeMode == "package" ? "BGCS.Runtime" : "Generated.NativeSequences.Runtime";
        string consumer = """
            using Generated.NativeSequences;
            public static class SequenceConsumer {
                public static int Run(string binary) {
                    using var api = SequenceApi.Load(binary);
                    if (api.SumVectorWithSequence(new int[] { 20, 22 }) != 42) return 1;
                    if (api.SumVectorWithSequence(global::System.ReadOnlySpan<int>.Empty) != 0) return 2;
                    if (api.SumArrayWithSequence(new int[] { 10, 12, 20 }) != 42) return 3;
                    if (api.SumStringWithSequence(new ushort[] { 65, 0, 66 }) != 134) return 4;
                    if (api.SumUtf8WithSequence(new byte[] { 65, 0, 195, 169 }) != 433) return 14;
                    if (api.Echo8(255) != 255) return 15;
                    if (api.SumBoolWithSequence(new byte[] { 0, 1, 2, 0 }) != 2) return 5;
                    int[] mutable = { 20, 20 };
                    if (api.SumSpanWithSequence(mutable) != 42 || mutable[0] != 21 || mutable[1] != 21) return 6;
                    if (api.SumConstSpanWithSequence(new int[] { 20, 22 }) != 42) return 7;
                    if (api.SumFixedSpanWithSequence(new int[] { 10, 12, 20 }) != 42) return 8;
                    if (api.SumRefWithSequence(new int[] { 20, 22 }) != 42) return 9;
                    using var receiver = api.CreateReceiver(2);
                    if (receiver.SumWithSequence(new int[] { 20, 20 }) != 42) return 10;
                    bool bounds = false;
                    try { api.SumArrayWithSequence(new int[] { 1, 2 }); }
                    catch (REPLACE_RUNTIME.NativeCallException) { bounds = true; }
                    if (!bounds) return 11;
                    bool nativeFailure = false;
                    try { receiver.SumWithSequence(new int[] { -1 }); }
                    catch (REPLACE_RUNTIME.NativeCallException) { nativeFailure = true; }
                    if (!nativeFailure) return 12;
                    receiver.Dispose();
                    return receiver.releaseFailure is null ? 42 : 13;
                }
            }
            """.Replace("REPLACE_RUNTIME", "global::" + runtime, StringComparison.Ordinal);
        CSharpCompilation compilation = CSharpCompilation.Create("NativeSequences_" + Guid.NewGuid().ToString("N"),
            Directory.EnumerateFiles(sources, "*.cs", SearchOption.AllDirectories).Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), new(LanguageVersion.CSharp14), path))
                .Append(CSharpSyntaxTree.ParseText(consumer, new(LanguageVersion.CSharp14))),
            references.Concat(runtimeMode == "package" ? [typeof(BGCS.Runtime.NativeModuleSession).Assembly.Location] : Array.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path)),
            new(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error));
        using MemoryStream image = new();
        var emitted = compilation.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        image.Position = 0;
        AssemblyLoadContext load = new("BGCS.NativeSequences.Test", isCollectible: true);
        try
        {
            Assembly assembly = load.LoadFromStream(image);
            Assert.Equal(42, assembly.GetType("SequenceConsumer", true)!.GetMethod("Run")!.Invoke(null, [binary]));
        }
        finally { load.Unload(); }
    }

    private const string Header = """
        #include <array>
        #include <span>
        #include <string>
        #include <vector>
        #include <stdexcept>
        namespace Demo {
        inline int SumVector(std::vector<int> values) { int sum = 0; for (int value : values) sum += value; return sum; }
        inline int SumArray(std::array<int, 3> values) { int sum = 0; for (int value : values) sum += value; return sum; }
        inline int SumString(std::u16string values) { int sum = static_cast<int>(values.size()); for (char16_t value : values) sum += value; return sum; }
        inline int SumUtf8(std::u8string values) { int sum = static_cast<int>(values.size()); for (char8_t value : values) sum += value; return sum; }
        inline char8_t Echo8(char8_t value) { return value; }
        inline int SumUtf8Span(std::span<const char8_t> values) { int sum = 0; for (char8_t value : values) sum += value; return sum; }
        inline int SumBool(std::vector<bool> values) { int sum = 0; for (bool value : values) sum += value; return sum; }
        inline int SumSpan(std::span<int> values) { int sum = 0; for (int& value : values) sum += ++value; return sum; }
        inline int SumConstSpan(std::span<const int> values) { int sum = 0; for (int value : values) sum += value; return sum; }
        inline int SumFixedSpan(std::span<const int, 3> values) { int sum = 0; for (int value : values) sum += value; return sum; }
        inline int SumRef(const std::vector<int>& values) { int sum = 0; for (int value : values) sum += value; return sum; }
        inline const int* Escape(std::vector<int> values) { return values.data(); }
        class Receiver {
            int m_bias;
        public:
            explicit Receiver(int bias) : m_bias(bias) {}
            ~Receiver() noexcept = default;
            int Sum(std::vector<int> values) const { int sum = m_bias; for (int value : values) { if (value < 0) throw std::runtime_error("negative"); sum += value; } return sum; }
        };
        }
        """;

    private static string WorkerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(directory!.FullName, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
