using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Analysis.Interop;
using BGCS.Core;
using BGCS.Core.Artifacts;
using BGCS.Core.Calls;
using BGCS.Interop.DotNet;
using BGCS.Targets.Desktop.Targeting;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.Core.Targets;
using BGCS.CSharp;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using BGCS.CSharp.Validation;
using Xunit;

namespace BGCS.Tests;

public sealed class VaListNeutralGenerationTests
{
    [Fact]
    public async Task Aapcs64StorageRetainsCompilerFieldsButDoesNotInventAnAggregateCallingProfile()
    {
        using Scratch scratch = new();
        File.WriteAllText(scratch.header, "typedef __builtin_va_list bgcs_va_list; void consume_value(bgcs_va_list args); void consume_pointer(const bgcs_va_list* args);\n");
        NativeModule native = await scratch.ParseAsync("linux-arm64-gnu");
        NativeDeclaration value = Assert.Single(native.declarations, declaration => declaration.name == "consume_value");
        NativeDeclaration pointer = Assert.Single(native.declarations, declaration => declaration.name == "consume_pointer");
        InteropModule pointerOnly = new CDeclarationAnalyzer().Analyze(native, "va_list", [pointer.id]);
        Assert.DoesNotContain(pointerOnly.diagnostics, diagnostic => diagnostic.isFailure);
        InteropType carrier = Assert.Single(pointerOnly.types, type => type.category == NativeTypeCategory.Record && type.size == 32 && type.fields.Length == 5);
        Assert.Equal(8, carrier.alignment);
        Assert.Equal(new long?[] { 0, 8, 16, 24, 28 }, carrier.fields.Select(field => field.offset));
        Assert.Equal(new long?[] { 8, 8, 8, 4, 4 }, carrier.fields.Select(field => pointerOnly.GetType(field.type).size));
        InteropModule byValue = new CDeclarationAnalyzer().Analyze(native, "va_list", [value.id]);
        ManagedApiModel valueApi = new DefaultManagedProjection().Project(byValue, new("Generated.VaList", "VaApi"), CancellationToken.None);
        ManagedApiModel pointerApi = new DefaultManagedProjection().Project(pointerOnly, new("Generated.VaList", "VaApi"), CancellationToken.None);
        foreach (BindingImportMode mode in Enum.GetValues<BindingImportMode>())
        {
            Sink forbidden = new();
            CSharpPlanningException failure = Assert.Throws<CSharpPlanningException>(() => new CSharpEmissionPlanner().Plan(valueApi, Request(valueApi, forbidden, mode)));
            Assert.Contains("calling", failure.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(forbidden.sources);
            Sink sink = new();
            CSharpEmissionPlan plan = new CSharpEmissionPlanner().Plan(pointerApi, Request(pointerApi, sink, mode));
            Assert.Empty(plan.aggregateCalls);
            Assert.EndsWith("*", Assert.Single(Assert.Single(plan.methods).parameterTypes), StringComparison.Ordinal);
            Assert.Equal(32, Assert.Single(plan.storage, storage => storage.nativeType == carrier.id).size);
            await new CSharpBackend().EmitAsync(pointerApi, Request(pointerApi, sink, mode), CancellationToken.None);
            if (mode != BindingImportMode.LibraryImport) AssertCarrierLayout(sink, plan, carrier, [0, 8, 16, 24, 28]);
        }
    }

    [Fact]
    public async Task SysVArraySpellingIsPreservedWhileTheCallableAbiUsesOnlyItsAdjustedPointer()
    {
        using Scratch scratch = new();
        File.WriteAllText(scratch.header, """
            typedef struct __va_list_tag {
                unsigned int gp_offset; unsigned int fp_offset;
                void* overflow_arg_area; void* reg_save_area;
            } __va_list_tag;
            typedef __va_list_tag va_list[1];
            void consume_args(va_list args);
            """);
        NativeModule native = await scratch.ParseAsync("linux-x64-gnu");
        NativeDeclaration function = Assert.Single(native.declarations, declaration => declaration.name == "consume_args");
        NativeDeclaration parameter = Assert.Single(function.children.Select(native.GetDeclaration), declaration => declaration.kind == "Parameter");
        Assert.Equal(NativeTypeCategory.Pointer, Underlying(native, parameter.type!.Value).category);
        NativeType written = Underlying(native, parameter.sourceType!.Value);
        Assert.Equal(NativeTypeCategory.Array, written.category);
        Assert.Equal(1, written.arrayLength);
        InteropModule abi = new CDeclarationAnalyzer().Analyze(native, "va_list", [function.id]);
        Assert.DoesNotContain(abi.diagnostics, diagnostic => diagnostic.isFailure);
        Assert.Equal(NativeTypeCategory.Pointer, abi.GetType(Assert.Single(Assert.Single(abi.functions).parameters).type).category);
        InteropType carrier = Assert.Single(abi.types, type => type.category == NativeTypeCategory.Record && type.size == 24 && type.fields.Length == 4);
        Assert.Equal(new long?[] { 0, 4, 8, 16 }, carrier.fields.Select(field => field.offset));
        ManagedApiModel api = new DefaultManagedProjection().Project(abi, new("Generated.VaList", "VaApi"), CancellationToken.None);
        foreach (BindingImportMode mode in Enum.GetValues<BindingImportMode>())
        {
            Sink sink = new();
            CSharpEmissionPlan plan = new CSharpEmissionPlanner().Plan(api, Request(api, sink, mode));
            Assert.Empty(plan.aggregateCalls);
            string raw = Assert.Single(Assert.Single(plan.methods).parameterTypes);
            Assert.True(raw == "nint" || raw.EndsWith("*", StringComparison.Ordinal), raw);
            await new CSharpBackend().EmitAsync(api, Request(api, sink, mode), CancellationToken.None);
            if (mode != BindingImportMode.LibraryImport) AssertCarrierLayout(sink, plan, carrier, [0, 4, 8, 16]);
        }
    }

    private static NativeType Underlying(
        NativeModule module,
        StableId identity
    ) {
        NativeType type = module.GetType(identity);
        HashSet<StableId> visited = [];
        while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified && type.elementType is StableId element && visited.Add(type.id)) type = module.GetType(element);
        return type;
    }
    private static EmissionRequest Request(
        ManagedApiModel model,
        Sink sink,
        BindingImportMode mode
    ) {
        NativeTargetDescriptor target = new TargetResolver().Resolve(new(new(model.interop.targetId)));
        ManagedRuntimeProfile runtime = TestComposition.ResolveRuntime(target);
        var plans = ImmutableArray.CreateBuilder<NativeCallPlan>();
        foreach (InteropFunction function in model.interop.functions)
        {
            CallAbiAssessment assessment = DotNetCallAbi.Assess(new(model.interop, target, runtime, NativeCallSignature.FromFunction(function)), runtime);
            if (assessment.disposition == NativeCallDisposition.Direct) plans.Add(assessment.plan!);
        }
        return new("va_list", sink) { importMode = mode, managedRuntime = runtime, callPlans = plans.ToImmutable() };
    }
    private static void AssertCarrierLayout(
        Sink sink,
        CSharpEmissionPlan plan,
        InteropType native,
        int[] offsets
    ) {
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("VaListStorage" + Guid.NewGuid().ToString("N"),
            sink.sources.Select(source => Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source.Value, path: source.Key)),
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                .Append(typeof(BGCS.Runtime.Aapcs64VaList).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(path)),
            new(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, generalDiagnosticOption: Microsoft.CodeAnalysis.ReportDiagnostic.Error));
        using MemoryStream image = new();
        var emitted = compilation.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        Assembly assembly = Assembly.Load(image.ToArray());
        string name = Assert.Single(plan.storage, storage => storage.nativeType == native.id).name;
        Type carrier = assembly.GetType("Generated.VaList." + name, throwOnError: true)!;
        Assert.Equal(native.size, Marshal.SizeOf(carrier));
        Assert.Equal(offsets, carrier.GetFields(BindingFlags.Instance | BindingFlags.Public).Select(field => Marshal.OffsetOf(carrier, field.Name).ToInt32()).Order());
    }
    private sealed class Sink : IArtifactSink
    {
        internal Dictionary<string, string> sources { get; } = new(StringComparer.Ordinal);
        public string WriteText(
            string relativePath,
            string content
        ) { sources.Add(relativePath, content); return relativePath; }
    }
    private sealed class Scratch : IDisposable
    {
        private readonly string m_root = Path.Combine(Path.GetTempPath(), "bgcs-neutral-va-list-" + Guid.NewGuid().ToString("N"));
        internal string header => Path.Combine(m_root, "native.h");
        internal Scratch() => Directory.CreateDirectory(m_root);
        internal async Task<NativeModule> ParseAsync(string target)
        {
            DirectoryInfo? repository = new(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "BindGen-CS.slnx"))) repository = repository.Parent;
            string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(Path.Combine(repository!.FullName,
                "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0")).Build();
            AnalysisResult result = await host.AnalyzeAsync(new([header], SourceLanguage.C, m_root) { targetId = target, parseMacros = false, parseSystemIncludes = true });
            Assert.True(result.success, result.GetDiagnosticSummary());
            return result.module!;
        }
        public void Dispose() => Directory.Delete(m_root, recursive: true);
    }
}
