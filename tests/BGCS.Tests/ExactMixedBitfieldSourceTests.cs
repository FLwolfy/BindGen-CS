using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Analysis.Interop;
using BGCS.Contracts;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.CSharp;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using BGCS.Infrastructure.Workers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.Tests;

public sealed class ExactMixedBitfieldSourceTests
{
    [Theory]
    [InlineData("windows-x64-msvc", "windows", "msvc", "x86_64-pc-windows-msvc")]
    [InlineData("linux-x64-gnu", "linux", "gnu", "x86_64-unknown-linux-gnu")]
    public async Task ActualTargetParsingPreservesTheExactMixedPackedAndBooleanSourceLayouts(string targetId, string platform, string abi, string triple)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-exact-bitfield-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "bits.h");
            await File.WriteAllTextAsync(header, """
                typedef enum Kind { KIND_ZERO = 0, KIND_ONE = 1 } Kind;
                typedef struct Mixed { unsigned int count:8; Kind kind:8; unsigned int offset:16; } Mixed;
                #pragma pack(push, 1)
                typedef struct Packed { unsigned int value:7; } Packed;
                typedef struct BooleanBits { _Bool enabled:1; } BooleanBits;
                typedef struct PackedValue { char tag; int value; } PackedValue;
                #pragma pack(pop)
                """);
            DirectoryInfo? repository = new(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "BindGen-CS.slnx"))) repository = repository.Parent;
            string build = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
            FrontendWorkerClient worker = new(Path.Combine(repository!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", build, "net10.0", "BGCS.Frontend.Clang.Worker.dll"));
            NativeTargetDescriptor target = new(new(targetId), platform, "x64", abi, triple, new(), byteOrder: NativeByteOrder.LittleEndian);
            NativeModule native = (await worker.ParseAsync(new(target, SourceLanguage.C, [header]) { pathBase = root, parseMacros = false }, CancellationToken.None)).module;
            Assert.DoesNotContain(native.diagnostics, diagnostic => diagnostic.isFailure);
            Assert.Empty(new NativeModuleVerifier().Validate(native));
            NativeType mixed = Record("Mixed"), packed = Record("Packed"), boolean = Record("BooleanBits"), packedValue = Record("PackedValue");
            Assert.Equal(4, mixed.size);
            Assert.Equal(new long?[] { 0, 8, 16 }, Fields(mixed).Select(field => field.bitOffset));
            Assert.Equal(new int?[] { 8, 8, 16 }, Fields(mixed).Select(field => field.bitWidth));
            Assert.Equal(7, Assert.Single(Fields(packed)).bitWidth);
            Assert.Equal(1, Assert.Single(Fields(boolean)).bitWidth);
            Assert.Equal(1, boolean.size);
            Assert.Equal(5, packedValue.size);
            Assert.Equal(1, packedValue.alignment);
            Assert.Equal(new long?[] { 0, 1 }, Fields(packedValue).Select(field => field.offset));
            var interop = new CDeclarationAnalyzer().Analyze(native, "exact-bitfield");
            Assert.DoesNotContain(interop.diagnostics, diagnostic => diagnostic.isFailure);
            var model = new DefaultManagedProjection().Project(interop, new("Generated.ExactBits", "BitsApi"), CancellationToken.None);
            foreach (BindingImportMode mode in Enum.GetValues<BindingImportMode>())
            {
                MemorySink sink = new();
                EmissionRequest request = new("bits", sink) { importMode = mode, includeRuntimeSource = true, runtimeNamespace = "Generated.ExactBits.LocalRuntime" };
                CSharpEmissionPlan plan = new CSharpEmissionPlanner().Plan(model, request);
                await new CSharpBackend().EmitAsync(model, request, CancellationToken.None);
                using MemoryStream image = Compile(sink.sources);
                AssemblyLoadContext load = new("exact-bitfield-consumer", isCollectible: true);
                try
                {
                    Assembly consumer = load.LoadFromStream(image);
                    foreach (NativeType record in new[] { mixed, packed, boolean, packedValue })
                    {
                        CSharpStoragePlan storage = Assert.Single(plan.storage, storage => storage.nativeType == record.id);
                        Type managed = consumer.GetType("Generated.ExactBits." + storage.name.TrimStart('@'), throwOnError: true)!;
                        Assert.Equal(record.size, Marshal.SizeOf(managed));
                        Assert.Equal(record.size, storage.size);
                        if (record == packedValue)
                            Assert.Equal(new long[] { 0, 1 }, storage.fields.Select(field => Marshal.OffsetOf(managed, field.name.TrimStart('@')).ToInt64()));
                    }
                    Assert.DoesNotContain(consumer.GetReferencedAssemblies(), reference => reference.Name?.StartsWith("BGCS", StringComparison.Ordinal) == true);
                }
                finally { load.Unload(); }
            }

            NativeType Record(string name) => Assert.Single(native.types, type => type.category == NativeTypeCategory.Record
                && type.declaration is { } declaration && native.GetDeclaration(declaration).name == name);
            NativeDeclaration[] Fields(NativeType record) => native.GetDeclaration(record.declaration!.Value).children.Select(native.GetDeclaration)
                .Where(declaration => declaration.kind == "Field").ToArray();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static MemoryStream Compile(Dictionary<string, string> sources)
    {
        string framework = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        string paths = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? throw new InvalidOperationException("Framework metadata is unavailable.");
        var references = paths.Split(Path.PathSeparator).Where(path => string.Equals(Path.GetDirectoryName(path), framework,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)).Select(path => MetadataReference.CreateFromFile(path));
        var trees = sources.Select(source => CSharpSyntaxTree.ParseText(source.Value, new CSharpParseOptions(LanguageVersion.CSharp14), source.Key));
        CSharpCompilation compilation = CSharpCompilation.Create("Exact.Bitfield.Source." + Guid.NewGuid().ToString("N"), trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable,
                generalDiagnosticOption: ReportDiagnostic.Error));
        MemoryStream image = new();
        var result = compilation.Emit(image);
        if (!result.Success) { image.Dispose(); Assert.Fail(string.Join(Environment.NewLine, result.Diagnostics)); }
        image.Position = 0;
        return image;
    }

    private sealed class MemorySink : IArtifactSink
    {
        internal Dictionary<string, string> sources { get; } = new(StringComparer.Ordinal);
        public string WriteText(string relativePath, string content) { sources.Add(relativePath, content); return relativePath; }
    }
}
