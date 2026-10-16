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

    [Theory]
    [InlineData("[DllImport(\"hidden\")] delegate int Hidden();")]
    [InlineData("class Extra { [LibraryImport(\"hidden\")] public int Value; }")]
    [InlineData("class Extra { public int Value { [DllImport(\"hidden\")] get => 0; } }")]
    [InlineData("class Extra { public void Method() { [DllImport(\"hidden\")] static extern int Hidden(); } }")]
    [InlineData("class Extra { public void Method() { System.Func<int> hidden = [DllImport(\"hidden\")] () => 0; } }")]
    [InlineData("struct Extra { public class Nested { [DllImport(\"hidden\")] public static extern int Hidden(); } }")]
    [InlineData("record Extra { [LibraryImport(\"hidden\")] public static partial int Hidden(); }")]
    [InlineData("[assembly: Foreign.DllImportAttribute(\"hidden\")] class Extra {}")]
    public async Task IndexedFunctionTableValidationRetainsTheGlobalImportAttributeGuard(string addition)
    {
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native_fixture", sink);
        var model = NeutralBackendTests.Model();
        CSharpEmissionPlan plan = new CSharpEmissionPlanner().Plan(model, request);
        await new CSharpBackend().EmitAsync(model, request, CancellationToken.None);
        sink.sources.Add("Additional.cs", addition);
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan,
            sink.sources.Select(source => (source.Key, source.Value)), request.libraryName));
        Assert.Contains("undeclared runtime imports", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("class Extra { const string Text = \"[DllImport(\\\"hidden\\\")]\"; }")]
    [InlineData("#if BGCS_INACTIVE_IMPORT\n[DllImport(\"hidden\")] class Hidden {}\n#endif\nclass Extra {}")]
    public async Task IndexedFunctionTableValidationIgnoresOnlySyntaxInertImportText(string addition)
    {
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native_fixture", sink);
        var model = NeutralBackendTests.Model();
        CSharpEmissionPlan plan = new CSharpEmissionPlanner().Plan(model, request);
        await new CSharpBackend().EmitAsync(model, request, CancellationToken.None);
        sink.sources.Add("Additional.cs", addition);
        new EmittedInteropContractReader().Verify(plan, sink.sources.Select(source => (source.Key, source.Value)), request.libraryName);
    }

    [Fact]
    public async Task ConcurrentValidationKeepsActualCandidateIndexesIndependentForTheSamePathsAndPlan()
    {
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native_fixture", sink);
        var model = NeutralBackendTests.Model();
        CSharpEmissionPlan plan = new CSharpEmissionPlanner().Plan(model, request);
        await new CSharpBackend().EmitAsync(model, request, CancellationToken.None);
        (string Path, string Text)[] original = sink.sources.Select(source => (source.Key, source.Value)).ToArray();
        (string Path, string Text)[] changed = original.Select(source => (source.Path,
            source.Text + "\n[Foreign.NativeNameAttribute] class UnexpectedMetadata {}\n")).ToArray();
        EmittedInteropContractReader reader = new();
        await Task.WhenAll(Enumerable.Range(0, 12).Select(index => Task.Run(() =>
        {
            if (index % 2 == 0) reader.Verify(plan, original, request.libraryName);
            else
            {
                InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => reader.Verify(plan, changed, request.libraryName));
                Assert.Contains("while metadata was disabled", failure.Message, StringComparison.Ordinal);
            }
        })));
        reader.Verify(plan, original, request.libraryName);
    }
}
