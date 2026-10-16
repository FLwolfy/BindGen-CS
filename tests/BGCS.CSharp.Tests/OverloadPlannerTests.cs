using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Managed;
using BGCS.Contracts.Native;
using BGCS.Contracts.Requests;
using BGCS.CSharp.Projection;
using BGCS.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class OverloadPlannerTests
{
    [Fact]
    public async Task DistinctParameterOverloads_CompileAndDispatchExactPublicAndRawSignatures()
    {
        ManagedApiModel model = Project(Module());
        Assert.All(model.methods, method => Assert.Equal("Evaluate", method.name));
        NeutralBackendTests.MemorySink sink = new();
        await new CSharpBackend().EmitAsync(model, new("unused", sink), CancellationToken.None);
        using CompiledSources compiled = Compile(sink);
        Type apiType = compiled.assembly.GetType("Generated.Overloads.OverloadApi", true)!;
        using IDisposable api = (IDisposable)apiType.GetMethod("FromProvider")!.InvokeFactory([new StaticModuleProvider(Exports())])!;
        Assert.Equal(42, apiType.GetMethod("Evaluate", [typeof(int)])!.Invoke(api, [40]));
        Assert.Equal(42.5, apiType.GetMethod("Evaluate", [typeof(double)])!.Invoke(api, [40.0]));
        object raw = apiType.GetProperty("Raw")!.GetValue(api)!;
        Assert.Equal(42, raw.GetType().GetMethod("Evaluate", [typeof(int)])!.Invoke(raw, [40]));
        Assert.Equal(42.5, raw.GetType().GetMethod("Evaluate", [typeof(double)])!.Invoke(raw, [40.0]));
    }

    [Fact]
    public async Task ManagedConversions_CannotMakeDistinctRawOverloadsAmbiguous()
    {
        ManagedApiModel model = Project(Module());
        model = model with { methods = model.methods.SetItem(1, model.methods[1] with
        {
            valueProjections = [new(model.interop.functions[1].parameters[0].id, new("global::System.Int32", "(double){value}"))]
        }) };
        NeutralBackendTests.MemorySink sink = new();
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await new CSharpBackend().EmitAsync(model, new("unused", sink), CancellationToken.None));
        Assert.Contains("Evaluate(System.Int32)", error.Message);
        Assert.Contains("native_eval_i32", error.Message);
        Assert.Contains("native_eval_f64", error.Message);
        Assert.Empty(sink.sources);
    }

    [Fact]
    public async Task TextReferenceNullability_DoesNotCreateDistinctFriendlyOverloads()
    {
        InteropModule original = Module();
        InteropType octet = new(new("octet"), "char", NativeTypeCategory.Primitive, 1, 1) { primitiveKind = "Char", isSigned = true };
        InteropType unit = new(new("unit"), "unsigned short", NativeTypeCategory.Primitive, 2, 2) { primitiveKind = "UnsignedShort", isSigned = false };
        InteropType utf8 = new(new("utf8"), "char*", NativeTypeCategory.Pointer, 8, 8) { elementType = octet.id };
        InteropType utf16 = new(new("utf16"), "unsigned short*", NativeTypeCategory.Pointer, 8, 8) { elementType = unit.id };
        Evidence evidence = new(EvidenceLevel.UserAsserted, "overload-fixture");
        StringContract Text(NativeTextEncoding encoding, int width, bool nullable) => new(encoding, width, NativeLengthUnit.CodeUnits,
            NativeParameterDirection.Input, new(NativeLifetimeKind.Call, null, evidence), evidence)
        { byteOrder = NativeTextByteOrder.Native, allowsNull = nullable, retainsPointer = false, zeroTerminated = true };
        InteropModule module = new(original.libraryId, original.targetId, original.pointerSize,
            original.types.AddRange(ImmutableArray.Create(octet, unit, utf8, utf16)),
            [new(new("read8"), "read8", original.types[0].id, [new(new("text8"), "text", utf8.id) { text = Text(NativeTextEncoding.Utf8, 1, false) }], "C") { sourceName = "Read" },
                new(new("read16"), "read16", original.types[0].id, [new(new("text16"), "text", utf16.id) { text = Text(NativeTextEncoding.Utf16, 2, true) }], "C") { sourceName = "Read" }], original.diagnostics)
        { byteOrder = original.byteOrder };
        NeutralBackendTests.MemorySink sink = new();
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await new CSharpBackend().EmitAsync(Project(module), new("unused", sink), CancellationToken.None));
        Assert.Contains("Read(System.String)", error.Message);
        Assert.Empty(sink.sources);
    }

    [Fact]
    public async Task ReturnTypeOnlyCollision_RequiresExplicitNamesBeforeAnyArtifactIsWritten()
    {
        InteropModule original = Module();
        InteropModule module = WithFunctions(original, original.functions.SetItem(1, original.functions[1] with
        { parameters = [original.functions[1].parameters[0] with { type = original.types[0].id }] }));
        NeutralBackendTests.MemorySink sink = new();
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await new CSharpBackend().EmitAsync(Project(module), new("unused", sink), CancellationToken.None));
        Assert.Contains("RawApi.Evaluate(System.Int32)", error.Message);
        Assert.Empty(sink.sources);
    }

    [Fact]
    public async Task ExplicitSignatureAndExportNames_ResolveAnOtherwiseImpossibleCollision()
    {
        InteropModule original = Module();
        InteropModule module = WithFunctions(original, original.functions.SetItem(1, original.functions[1] with
        { parameters = [original.functions[1].parameters[0] with { type = original.types[0].id }], sourceSignature = "Demo::Evaluate(long)" }));
        ManagedProjectionRequest request = new("Generated.Overloads", "OverloadApi") { names = ImmutableDictionary<string, string>.Empty
            .Add("Demo::Evaluate", "Evaluate").Add("Demo::Evaluate(long)", "EvaluateLong").Add("native_eval_f64", "EvaluateOther") };
        ManagedApiModel model = new DefaultManagedProjection().Project(module, request, CancellationToken.None);
        Assert.Equal("EvaluateOther", model.methods[1].name);
        NeutralBackendTests.MemorySink sink = new();
        await new CSharpBackend().EmitAsync(model, new("unused", sink), CancellationToken.None);
        using CompiledSources compiled = Compile(sink);
        Assert.NotNull(compiled.assembly.GetType("Generated.Overloads.OverloadApi", true)!.GetMethod("EvaluateOther"));
    }

    private static ManagedApiModel Project(InteropModule module) => new DefaultManagedProjection().Project(module, new("Generated.Overloads", "OverloadApi"), CancellationToken.None);
    private static InteropModule WithFunctions(InteropModule original, ImmutableArray<InteropFunction> functions)
        => new(original.libraryId, original.targetId, original.pointerSize, original.types, functions, original.diagnostics) { byteOrder = original.byteOrder };
    private static InteropModule Module()
    {
        InteropType integer = new(new("integer"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropType real = new(new("real"), "double", NativeTypeCategory.Primitive, 8, 8) { primitiveKind = "Double" };
        return new("overloads", "windows-x64-msvc", 8, [integer, real],
            [new(new("eval_i32"), "native_eval_i32", integer.id, [new(new("i32"), "value", integer.id)], "C")
            { sourceName = "Evaluate", sourceQualifiedName = "Demo::Evaluate", sourceSignature = "Demo::Evaluate(int)", sourceRole = "Function" },
                new(new("eval_f64"), "native_eval_f64", real.id, [new(new("f64"), "value", real.id)], "C")
            { sourceName = "Evaluate", sourceQualifiedName = "Demo::Evaluate", sourceSignature = "Demo::Evaluate(double)", sourceRole = "Function" }], []);
    }
    private static unsafe IEnumerable<KeyValuePair<string, nint>> Exports() => new Dictionary<string, nint>
    {
        ["native_eval_i32"] = (nint)(delegate* unmanaged[Cdecl]<int, int>)&EvaluateInt,
        ["native_eval_f64"] = (nint)(delegate* unmanaged[Cdecl]<double, double>)&EvaluateDouble
    };
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] private static int EvaluateInt(int value) => value + 2;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] private static double EvaluateDouble(double value) => value + 2.5;

    private static CompiledSources Compile(NeutralBackendTests.MemorySink sink)
    {
        string[] paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        CSharpCompilation compilation = CSharpCompilation.Create("NativeOverloads_" + Guid.NewGuid().ToString("N"),
            sink.sources.Select(source => CSharpSyntaxTree.ParseText(source.Value, new(LanguageVersion.CSharp14), source.Key)),
            paths.Append(typeof(NativeModuleSession).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path)),
            new(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error));
        using MemoryStream stream = new();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        stream.Position = 0;
        AssemblyLoadContext context = new("BGCS.NativeOverloads.Test", isCollectible: true);
        return new(context, context.LoadFromStream(stream));
    }
    private sealed record CompiledSources(AssemblyLoadContext context, Assembly assembly) : IDisposable { public void Dispose() => context.Unload(); }
}
