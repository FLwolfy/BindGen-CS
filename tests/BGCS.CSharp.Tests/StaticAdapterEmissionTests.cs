using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Validation;
using BGCS.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class StaticAdapterEmissionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task StaticProfile_EmitsCheckedAdapterAndCompilesWithoutDynamicLoading(bool sourceRuntime, bool singleFile)
    {
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = Request(sink) with
        { includeRuntimeSource = sourceRuntime, runtimeNamespace = sourceRuntime ? "Generated.Example.Runtime" : "BGCS.Runtime", singleFile = singleFile };
        await new CSharpBackend().EmitAsync(NeutralBackendTests.Model(), request, CancellationToken.None);
        string text = string.Join("\n", sink.sources.Values);
        Assert.Contains("FromStatic()", text);
        Assert.Contains("StaticNativeAdapter.GetRevision()", text);
        Assert.Contains("identity[expected.Length] != 0", text);
        Assert.DoesNotContain("DynamicModuleProvider", text);
        Assert.DoesNotContain(" Load(string", text);
        string[] framework = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        CSharpCompilation compilation = CSharpCompilation.Create("StaticAdapterConsumer", sink.sources.Select(source => CSharpSyntaxTree.ParseText(source.Value, new(LanguageVersion.CSharp14), source.Key)),
            framework.Append(typeof(NativeModuleSession).Assembly.Location).Distinct().Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable));
        using MemoryStream output = new();
        var result = compilation.Emit(output);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
    }

    [Theory]
    [InlineData("symbol")]
    [InlineData("carrier")]
    [InlineData("convention")]
    [InlineData("library")]
    public async Task ProcessedAdapterImports_MustRetainTheExactStaticNativeContract(string change)
    {
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = Request(sink);
        CSharpEmissionPlan plan = new CSharpEmissionPlanner().Plan(NeutralBackendTests.Model(), request);
        await new CSharpBackend().EmitAsync(NeutralBackendTests.Model(), request, CancellationToken.None);
        var changed = sink.sources.Select(source => (source.Key, change switch
        {
            "symbol" => source.Value.Replace("bgcs_example_get_export", "wrong_export", StringComparison.Ordinal),
            "carrier" => source.Value.Replace("extern nuint GetExport", "extern uint GetExport", StringComparison.Ordinal),
            "convention" => source.Value.Replace("CallingConvention.Cdecl", "CallingConvention.StdCall", StringComparison.Ordinal),
            "library" => source.Value.Replace("\"__Internal\"", "\"wrong_module\"", StringComparison.Ordinal),
            _ => throw new ArgumentException(nameof(change))
        }));
        Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan, changed, request.libraryName));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("order")]
    [InlineData("revision")]
    public void InvalidStaticTable_FailsBeforeAnyCandidateWrites(string fault)
    {
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = Request(sink);
        request = request with { staticExportTable = fault switch
        {
            "missing" => null,
            "order" => request.staticExportTable! with { exportNames = request.staticExportTable.exportNames.Reverse().ToImmutableArray() },
            "revision" => request.staticExportTable! with { revision = 2 },
            _ => throw new ArgumentException(nameof(fault))
        } };
        Assert.ThrowsAny<Exception>(() => new CSharpEmissionPlanner().Plan(NeutralBackendTests.Model(), request));
        Assert.Empty(sink.sources);
    }

    private static EmissionRequest Request(NeutralBackendTests.MemorySink sink) => new("__Internal", sink)
    {
        managedRuntime = new("static-fixture", ManagedRuntimeKind.NativeAot, NativeModuleMechanism.StaticAdapter,
            [BindingImportMode.FunctionTable], true, false),
        staticExportTable = new("bgcs_example", "bgcs_example_get_export", "bgcs_example_get_revision", "bgcs_example_get_count",
            "bgcs_example_get_abi_identity", "bgcs_example_get_pointer_size", new string('a', 64),
            NeutralBackendTests.Model().interop.functions.Select(function => function.exportName).ToImmutableArray())
    };
}
