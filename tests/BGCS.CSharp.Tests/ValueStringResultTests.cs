using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class ValueStringResultTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false, NativeTextEncoding.Utf8)]
    [InlineData(BindingImportMode.DllImport, false, NativeTextEncoding.Utf8)]
    [InlineData(BindingImportMode.LibraryImport, false, NativeTextEncoding.Utf8)]
    [InlineData(BindingImportMode.FunctionTable, true, NativeTextEncoding.Utf8)]
    [InlineData(BindingImportMode.DllImport, true, NativeTextEncoding.Utf8)]
    [InlineData(BindingImportMode.LibraryImport, true, NativeTextEncoding.Utf8)]
    [InlineData(BindingImportMode.FunctionTable, false, NativeTextEncoding.Utf16)]
    [InlineData(BindingImportMode.DllImport, false, NativeTextEncoding.Utf16)]
    [InlineData(BindingImportMode.LibraryImport, false, NativeTextEncoding.Utf16)]
    [InlineData(BindingImportMode.FunctionTable, true, NativeTextEncoding.Utf16)]
    [InlineData(BindingImportMode.DllImport, true, NativeTextEncoding.Utf16)]
    [InlineData(BindingImportMode.LibraryImport, true, NativeTextEncoding.Utf16)]
    [InlineData(BindingImportMode.FunctionTable, false, NativeTextEncoding.Utf32)]
    [InlineData(BindingImportMode.DllImport, false, NativeTextEncoding.Utf32)]
    [InlineData(BindingImportMode.LibraryImport, false, NativeTextEncoding.Utf32)]
    [InlineData(BindingImportMode.FunctionTable, true, NativeTextEncoding.Utf32)]
    [InlineData(BindingImportMode.DllImport, true, NativeTextEncoding.Utf32)]
    [InlineData(BindingImportMode.LibraryImport, true, NativeTextEncoding.Utf32)]
    public async Task FreshTextCopiesUseOneRetainedCallAndOneBoundedBulkCopy(
        BindingImportMode mode,
        bool sourceRuntime,
        NativeTextEncoding encoding
    ) {
        InteropModule module = Module(encoding);
        ManagedApiModel model = Project(module);
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("value_strings", sink)
        {
            importMode = mode,
            includeRuntimeSource = sourceRuntime,
            runtimeNamespace = sourceRuntime ? "Generated.ValueStrings.Runtime" : "BGCS.Runtime",
            maximumValueStringCodeUnits = 73
        };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        CSharpValueStringResultPlan result = Assert.Single(plan.valueStringResults);
        Assert.Equal(73, result.maximumCodeUnits);
        Assert.DoesNotContain(plan.statusMethods, status => status.methodIndex == result.factory.methodIndex);
        Assert.Null(module.functions[0].parameters[0].ownership!.releaseThreadDomain);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        MethodDeclarationSyntax method = Assert.Single(sink.sources.SelectMany(pair => CSharpSyntaxTree.ParseText(pair.Value).GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()),
            declaration => declaration.Identifier.ValueText == "Make" && declaration.ReturnType.ToString() == "string");
        string source = method.ToFullString();
        Assert.Equal(1, source.Split(".AcquireResource()", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, source.Split(".AcquireCall()", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("this.Raw.", source, StringComparison.Ordinal);
        Assert.Contains("RecordUnresolvedCleanup", source, StringComparison.Ordinal);
        const string consumer = "namespace Generated.ValueStrings; public static class Consumer { public static string Read(Api api)=>api.Make(); }";
        if (mode == BindingImportMode.LibraryImport) await ValueReceiverTests.CompileWithSdkAsync(sink, consumer, sourceRuntime);
        else
        {
            CSharpCompilation compilation = CSharpCompilation.Create("ValueString_" + Guid.NewGuid().ToString("N"),
                sink.sources.Select(pair => CSharpSyntaxTree.ParseText(pair.Value, new(LanguageVersion.CSharp14), pair.Key)).Append(CSharpSyntaxTree.ParseText(consumer)),
                ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Append(typeof(BGCS.Runtime.NativeModuleSession).Assembly.Location)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path)),
                new(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error));
            using MemoryStream image = new();
            var emitted = compilation.Emit(image);
            Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        }
        new EmittedInteropContractReader().Verify(plan, sink.sources.Select(pair => (pair.Key, pair.Value)), request.libraryName);
        foreach (string token in new[] { "AcquireResource()", "RecordUnresolvedCleanup(", ".DecodeLength(", " != __bgcsNativeTextCount", "73UL" })
        {
            string replacement = token switch { "73UL" => "73000UL", " != __bgcsNativeTextCount" => " == __bgcsNativeTextCount", _ => "Changed" + token };
            var changed = sink.sources.Select(pair => (pair.Key, pair.Value.Replace(token, replacement, StringComparison.Ordinal))).ToArray();
            Assert.Contains(changed, pair => pair.Item2 != sink.sources[pair.Key]);
            Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan, changed, request.libraryName));
        }
    }

    [Theory]
    [InlineData("unknown-encoding")]
    [InlineData("unknown-release-scope")]
    [InlineData("wrong-owned-release")]
    [InlineData("asynchronous")]
    public void MissingTextOrCreatingCallProofKeepsTheExactRawOwnedResult(string fault)
    {
        InteropModule original = Module(NativeTextEncoding.Utf8);
        var types = original.types;
        var functions = original.functions;
        int holderIndex = types.IndexOf(types.Single(type => type.id == new StableId("holder")));
        NativeValueProtocol protocol = types[holderIndex].valueProtocol!;
        if (fault == "unknown-encoding") protocol = protocol with { text = protocol.text! with { encoding = NativeTextEncoding.Unknown, encodingEvidence = new(EvidenceLevel.Unknown, "not-declared") } };
        if (fault == "unknown-release-scope") protocol = protocol with { scopedRelease = null };
        types = types.SetItem(holderIndex, types[holderIndex] with { valueProtocol = protocol });
        if (fault == "wrong-owned-release") functions = functions.SetItem(0, functions[0] with
        {
            parameters = functions[0].parameters.SetItem(0,
            functions[0].parameters[0] with { ownership = functions[0].parameters[0].ownership! with { releaseFunction = new("other-release") } })
        });
        if (fault == "asynchronous") functions = functions.SetItem(0, functions[0] with { asynchronous = new(new("completion"), NativeQuiescenceKind.NoActiveNativeInvocations, protocol.evidence) });
        InteropModule module = new(original.libraryId, original.targetId, original.pointerSize, types, functions, []) { byteOrder = original.byteOrder };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(Project(module), new("value_strings", new NeutralBackendTests.MemorySink()));
        Assert.Empty(plan.valueStringResults);
        Assert.Contains(plan.methods, method => method.nativeExport == new StableId("make"));
        Assert.Contains(plan.ownedProjectionOmissions, omission => omission.nativeExport == new StableId("make"));
    }

    internal static ManagedApiModel Project(InteropModule module) => new DefaultManagedProjection().Project(module, new("Generated.ValueStrings", "Api"), CancellationToken.None);

    internal static InteropModule Module(NativeTextEncoding encoding)
    {
        Evidence facts = new(EvidenceLevel.Declared, "fixture:exact-native-text-value"), asserted = new(EvidenceLevel.UserAsserted, "fixture:encoding");
        int width = encoding == NativeTextEncoding.Utf8 ? 1 : encoding == NativeTextEncoding.Utf16 ? 2 : 4;
        InteropType none = new(new("void"), "void", NativeTypeCategory.Primitive, 0, 1) { primitiveKind = "Void" };
        InteropType status = new(new("status"), "int32_t", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropType count = new(new("count"), "uint64_t", NativeTypeCategory.Primitive, 8, 8) { primitiveKind = "UnsignedLongLong", isSigned = false };
        InteropType unit = new(new("unit"), "unit", NativeTypeCategory.Primitive, width, width)
        { primitiveKind = width == 1 ? "UnsignedChar" : width == 2 ? "UnsignedShort" : "UnsignedInt", isSigned = false };
        InteropType holder = new(new("holder"), "Holder", NativeTypeCategory.Record, null, null) { sourceNativeType = new("source-holder") };
        InteropType constant = new(new("constant-holder"), "const Holder", NativeTypeCategory.Qualified, null, null) { elementType = holder.id, qualifier = "Const" };
        InteropType error = new(new("error"), "Error", NativeTypeCategory.Record, null, null);
        InteropType Pointer(
            string id,
            StableId element
        ) => new(new(id), id, NativeTypeCategory.Pointer, 8, 8) { elementType = element };
        InteropType holderPointer = Pointer("holder-pointer", holder.id), constPointer = Pointer("const-pointer", constant.id), resultPointer = Pointer("result-pointer", holderPointer.id);
        InteropType errorPointer = Pointer("error-pointer", error.id), errorOutput = Pointer("error-output", errorPointer.id), countOutput = Pointer("count-output", count.id), unitPointer = Pointer("unit-pointer", unit.id);
        InteropParameter Receiver(
            string id,
            bool mutable = false
        ) => new(new(id), "self", mutable ? holderPointer.id : constPointer.id)
        { ownership = new(NativeOwnershipKind.Borrowed, new(NativeLifetimeKind.Call, null, facts), facts) { retainsPointer = false } };
        InteropFunction Status(
            string id,
            ImmutableArray<InteropParameter> inputs,
            StableId? outputType
        ) {
            InteropParameter? output = outputType is StableId type ? new(new(id + "-result"), "result", type) : null;
            if (id == "make") output = output! with
            {
                ownership = new(NativeOwnershipKind.Owned, new(NativeLifetimeKind.Owner, output!.id, facts), facts)
                { releaseFunction = new("destroy"), allocator = new("default-native-value") }
            };
            StableId errorId = new(id + "-error");
            OwnershipContract ownership = new(NativeOwnershipKind.Owned, new(NativeLifetimeKind.Owner, errorId, facts), facts)
            { releaseFunction = new("error-free"), allocator = new("native-error-domain"), releaseThreadDomain = "any" };
            InteropParameter errorParameter = new(errorId, "error", errorOutput.id) { ownership = ownership };
            ImmutableArray<InteropParameter> parameters = output is null ? inputs : inputs.Add(output);
            return new(new(id), id == "make" ? "Make" : id, status.id, parameters.Add(errorParameter), "C")
            { error = new("0", facts) { valueParameter = output?.id, errorParameter = errorId, errorOwnership = ownership } };
        }
        InteropFunction make = Status("make", [], resultPointer.id);
        InteropFunction length = Status("length", [Receiver("length-self")], countOutput.id);
        InteropFunction get = Status("get", [Receiver("get-self"), new(new("get-index"), "index", count.id)], unitPointer.id);
        InteropFunction copy = Status("copy", [Receiver("copy-self"), new(new("copy-destination"), "destination", unitPointer.id), new(new("copy-capacity"), "capacity", count.id)], countOutput.id);
        InteropFunction destroy = Status("destroy", [Receiver("destroy-self", true)], null);
        InteropFunction free = new(new("error-free"), "error_free", none.id, [new(new("free-error"), "error", errorPointer.id)], "C");
        holder = holder with
        {
            valueProtocol = new(new("protocol"), NativeValueFamily.String, holder.id, holder.sourceNativeType!.Value,
            [new(NativeValueOperationKind.Count, length.id, new("length-self"), []), new(NativeValueOperationKind.GetAt, get.id, new("get-self"), [new("get-index")]),
                new(NativeValueOperationKind.CopyCodeUnits, copy.id, new("copy-self"), [new("copy-destination"), new("copy-capacity")])], [new(new("source-unit"), unit.id, false)], facts)
            {
                releaseExport = destroy.id,
                scopedRelease = new(NativeValueReleaseScope.CreatingCall, facts),
                sequence = new(facts),
                text = new(width, false, NativeByteOrder.LittleEndian, facts) { encoding = encoding, encodingEvidence = asserted }
            }
        };
        return new("value-strings", "windows-x64-msvc", 8,
            [none, status, count, unit, holder, constant, error, holderPointer, constPointer, resultPointer, errorPointer, errorOutput, countOutput, unitPointer],
            [make, length, get, copy, destroy, free], [])
        { byteOrder = NativeByteOrder.LittleEndian };
    }
}
