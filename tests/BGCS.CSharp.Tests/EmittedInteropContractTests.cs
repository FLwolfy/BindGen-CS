using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts;
using BGCS.Contracts.Requests;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Validation;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class EmittedInteropContractTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task CompleteSources_AreRecheckedForAllImportModesAndLayouts(BindingImportMode mode, bool singleFile)
    {
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native_fixture", sink) { importMode = mode, singleFile = singleFile };
        var model = NeutralBackendTests.ModelWithStorage();
        CSharpEmissionPlan plan = new CSharpEmissionPlanner().Plan(model, request);
        await new CSharpBackend().EmitAsync(model, request, CancellationToken.None);
        new EmittedInteropContractReader().Verify(plan, sink.sources.Select(pair => (pair.Key, pair.Value)), request.libraryName);
    }

    [Theory]
    [InlineData("offset")]
    [InlineData("size")]
    [InlineData("pack")]
    [InlineData("enum")]
    [InlineData("array")]
    public async Task EditedStorage_RejectsChangedOffsetsExtentsAndIntegralCarriers(string change)
    {
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native_fixture", sink);
        var model = NeutralBackendTests.ModelWithStorage();
        CSharpEmissionPlan plan = new CSharpEmissionPlanner().Plan(model, request);
        await new CSharpBackend().EmitAsync(model, request, CancellationToken.None);
        var edited = sink.sources.Select(pair => (pair.Key, change switch
        {
            "offset" => pair.Value.Replace("FieldOffset(32)", "FieldOffset(31)", StringComparison.Ordinal),
            "size" => pair.Value.Replace("Size = 96", "Size = 97", StringComparison.Ordinal),
            "pack" => pair.Value.Replace("Pack = 1", "Pack = 8", StringComparison.Ordinal),
            "enum" => pair.Value.Replace("enum Flags : ulong", "enum Flags : uint", StringComparison.Ordinal),
            "array" => pair.Value.Replace("InlineArray(3)", "InlineArray(4)", StringComparison.Ordinal),
            _ => throw new ArgumentException(nameof(change))
        }));
        Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan, edited, request.libraryName));
    }

    [Theory]
    [InlineData(BindingImportMode.FunctionTable, "carrier")]
    [InlineData(BindingImportMode.FunctionTable, "convention")]
    [InlineData(BindingImportMode.FunctionTable, "export")]
    [InlineData(BindingImportMode.FunctionTable, "shared-field")]
    [InlineData(BindingImportMode.DllImport, "carrier")]
    [InlineData(BindingImportMode.DllImport, "convention")]
    [InlineData(BindingImportMode.DllImport, "export")]
    [InlineData(BindingImportMode.DllImport, "library")]
    [InlineData(BindingImportMode.LibraryImport, "carrier")]
    [InlineData(BindingImportMode.LibraryImport, "convention")]
    [InlineData(BindingImportMode.LibraryImport, "export")]
    [InlineData(BindingImportMode.LibraryImport, "library")]
    public async Task EditedActualImports_AreRejectedEvenWhenTheOriginalPlanIsUnchanged(BindingImportMode mode, string change)
    {
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native_fixture", sink) { importMode = mode };
        var model = NeutralBackendTests.Model();
        CSharpEmissionPlan plan = new CSharpEmissionPlanner().Plan(model, request);
        await new CSharpBackend().EmitAsync(model, request, CancellationToken.None);
        (string Path, string Text)[] edited = sink.sources.Select(pair => (pair.Key, change switch
        {
            "carrier" => pair.Value.Replace("<int, int, int>", "<long, int, int>", StringComparison.Ordinal).Replace("int Function0(int", "int Function0(long", StringComparison.Ordinal),
            "convention" => pair.Value.Replace("Cdecl", "Stdcall", StringComparison.Ordinal),
            "export" => pair.Value.Replace("\"add\"", "\"different_export\"", StringComparison.Ordinal),
            "library" => pair.Value.Replace("\"native_fixture\"", "\"different_library\"", StringComparison.Ordinal),
            "shared-field" => pair.Value.Replace("private readonly delegate*", "private static delegate*", StringComparison.Ordinal),
            _ => throw new ArgumentException(nameof(change))
        })).ToArray();
        Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan, edited, request.libraryName));
    }
}
