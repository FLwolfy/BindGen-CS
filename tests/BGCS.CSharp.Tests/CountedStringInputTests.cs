using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Analysis.Safety;
using BGCS.Core;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using BGCS.CSharp.Validation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class CountedStringInputTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task CountedTextHidesOnlyItsCheckedLengthAndKeepsTheRawAbi(BindingImportMode mode, bool sourceRuntime)
    {
        InteropModule module = Module();
        ManagedApiModel model = new DefaultManagedProjection().Project(module, new("Generated.Counted", "CountedApi"), CancellationToken.None);
        Assert.Equal(new StableId("data"), Assert.Single(Assert.Single(model.methods).stringInputs));
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("counted_native", sink) { importMode = mode, includeRuntimeSource = sourceRuntime,
            runtimeNamespace = sourceRuntime ? "Generated.Counted.Runtime" : "BGCS.Runtime" };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        CSharpStringInputPlan text = Assert.Single(Assert.Single(plan.methods).stringInputs);
        Assert.Equal(1, text.lengthIndex);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        const string consumer = """
            namespace Generated.Counted;
            public static unsafe class Consumer {
                public static int Friendly(CountedApi api,string value) => api.ReadText(value);
                public static int Raw(CountedApi api,byte* data,uint count) => api.Raw.ReadText(data,count);
            }
            """;
        if (mode == BindingImportMode.LibraryImport) await ValueReceiverTests.CompileWithSdkAsync(sink, consumer, sourceRuntime);
        else
        {
            CSharpCompilation compilation = CSharpCompilation.Create("Counted_" + Guid.NewGuid().ToString("N"),
                sink.sources.Select(pair => CSharpSyntaxTree.ParseText(pair.Value, new(LanguageVersion.CSharp14), pair.Key)).Append(CSharpSyntaxTree.ParseText(consumer)),
                ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                    .Append(typeof(BGCS.Runtime.NativeModuleSession).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path)),
                new(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error));
            using MemoryStream image = new();
            var emitted = compilation.Emit(image);
            Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        }
        new EmittedInteropContractReader().Verify(plan, sink.sources.Select(pair => (pair.Key, pair.Value)), request.libraryName);
        foreach (string from in new[] { ".encodedCodeUnitCount", "EncodeCounted(", "checked((uint)" })
        {
            string replacement = from switch { ".encodedCodeUnitCount" => ".pointer", "EncodeCounted(" => "EncodeNullTerminated(", _ => "unchecked((uint)" };
            var changed = sink.sources.Select(pair => (pair.Key, pair.Value.Replace(from, replacement, StringComparison.Ordinal))).ToArray();
            Assert.Contains(changed, pair => pair.Item2 != sink.sources[pair.Key]);
            Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan, changed, request.libraryName));
        }
    }

    [Theory]
    [InlineData("unknown-count")]
    [InlineData("self-count")]
    [InlineData("floating-count")]
    [InlineData("inferred")]
    [InlineData("unknown-termination")]
    [InlineData("dual-termination")]
    [InlineData("retained")]
    [InlineData("output-count")]
    [InlineData("shared-count")]
    [InlineData("zero-bound")]
    [InlineData("competing-buffer")]
    [InlineData("competing-buffer-other-count")]
    [InlineData("competing-value")]
    [InlineData("competing-callback")]
    [InlineData("asynchronous")]
    public void UnknownOrConflictingExtentsDoNotGainTemporaryStringConversions(string fault)
    {
        InteropModule module = Module();
        InteropFunction function = module.functions[0];
        InteropParameter parameter = function.parameters[0];
        StringContract text = parameter.text!;
        text = fault switch
        {
            "unknown-count" => text with { lengthParameter = new("missing") },
            "self-count" => text with { lengthParameter = parameter.id },
            "inferred" => text with { evidence = new(EvidenceLevel.Inferred, "test:guess") },
            "unknown-termination" => text with { zeroTerminated = null },
            "dual-termination" => text with { zeroTerminated = true },
            "retained" => text with { retainsPointer = true },
            "output-count" => text with { lengthDirection = NativeParameterDirection.Output },
            "zero-bound" => text with { maximumCodeUnits = 0 },
            _ => text
        };
        parameter = parameter with { text = text };
        if (fault is "competing-buffer" or "competing-buffer-other-count") parameter = parameter with { buffer = new(new("unit"), NativeLengthUnit.Bytes, NativeParameterDirection.Input, text.lifetime, text.evidence)
            { lengthParameter = new(fault == "competing-buffer" ? "length" : "other-count"), retainsPointer = false, allowsNull = false } };
        if (fault == "competing-value") parameter = parameter with { singleValue = new(NativeParameterDirection.Input, text.lifetime, text.evidence)
            { retainsPointer = false, allowsNull = false } };
        if (fault == "competing-callback") parameter = parameter with { callback = new(parameter.type, text.lifetime, text.evidence) };
        function = function with { parameters = function.parameters.SetItem(0, parameter) };
        if (fault == "competing-buffer-other-count") function = function with { parameters = function.parameters.Add(new(new("other-count"), "otherCount", new("count"))) };
        if (fault == "asynchronous") function = function with { asynchronous = new(new("completion"), NativeQuiescenceKind.NoActiveNativeInvocations, text.evidence) };
        if (fault == "shared-count") function = function with { parameters = function.parameters.Add(parameter with { id = new("other"), name = "other" }) };
        var types = fault == "floating-count" ? module.types.Select(type => type.id == new StableId("count")
            ? type with { primitiveKind = "Float" } : type).ToImmutableArray() : module.types;
        module = new(module.libraryId, module.targetId, module.pointerSize, types, [function], []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        Assert.NotNull(new FriendlyApiEligibilityAnalyzer().GetTextInputRestriction(module, function, parameter));
        if (parameter.buffer is not null) Assert.NotNull(new FriendlyApiEligibilityAnalyzer().GetBufferInputRestriction(module, function, parameter));
        ManagedApiModel model = new DefaultManagedProjection().Project(module, new("Generated.Counted", "CountedApi"), CancellationToken.None);
        Assert.Empty(Assert.Single(model.methods).stringInputs);
        Assert.Equal(function.parameters.Length, Assert.Single(model.methods).parameterNames.Length);
    }

    [Fact]
    public void ExplicitUtf8OnSignedCodeUnitsKeepsItsExactRawPointerType()
    {
        InteropModule original = Module();
        InteropModule module = new(original.libraryId, original.targetId, original.pointerSize, original.types.Select(type => type.id == new StableId("unit")
            ? type with { primitiveKind = "SignedChar", nativeName = "signed char", isSigned = true } : type).ToImmutableArray(), original.functions, []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        ManagedApiModel model = new DefaultManagedProjection().Project(module, new("Generated.Counted", "CountedApi"), CancellationToken.None);
        Assert.Single(Assert.Single(model.methods).stringInputs);
        CSharpMethodPlan method = Assert.Single(new CSharpTestPlanner().Plan(model, new("native", new NeutralBackendTests.MemorySink())).methods);
        Assert.Equal("sbyte*", method.parameterTypes[0]);
        Assert.Equal(NativeTextEncoding.Utf8, Assert.Single(method.stringInputs).encoding);
    }

    private static InteropModule Module()
    {
        Evidence evidence = new(EvidenceLevel.UserAsserted, "test:counted-input-call-only");
        InteropType unit = new(new("unit"), "unsigned char", NativeTypeCategory.Primitive, 1, 1) { primitiveKind = "UnsignedChar", isSigned = false };
        InteropType pointer = new(new("pointer"), "const unsigned char*", NativeTypeCategory.Pointer, 8, 8) { elementType = unit.id };
        InteropType count = new(new("count"), "unsigned int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "UnsignedInt", isSigned = false };
        InteropType result = new(new("result"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropParameter data = new(new("data"), "data", pointer.id) { text = new(NativeTextEncoding.Utf8, 1, NativeLengthUnit.CodeUnits,
            NativeParameterDirection.Input, new(NativeLifetimeKind.Call, null, evidence), evidence)
        { zeroTerminated = false, retainsPointer = false, allowsNull = false, lengthParameter = new("length"), lengthDirection = NativeParameterDirection.Input } };
        return new("counted", "windows-x64-msvc", 8, [unit, pointer, count, result],
            [new(new("read"), "ReadText", result.id, [data, new(new("length"), "length", count.id)], "C")], []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
    }
}
