using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Analysis.Layout;
using BGCS.Contracts.Common;
using BGCS.Contracts;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Managed;
using BGCS.Contracts.Native;
using BGCS.Contracts.Requests;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using BGCS.CSharp.Validation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class ExternalCarrierPlanningTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExternalFieldCopiesComposeWithNamespaceDependentRecipeHelpers(bool singleFile)
    {
        (InteropModule module, ExternalTypeProjection projection) = Create();
        ManagedApiModel model = new DefaultManagedProjection().Project(module, new("Generated.ExternalHelpers", "Api"), CancellationToken.None);
        model = model with { externalTypeProjections = [projection], recipeHelpers =
            [new("vector", "float", new("Vector2", "{value}.X", "new Vector2({value}, 0)", "System.Numerics"))] };
        NamespaceSink sink = new();
        await new CSharpBackend().EmitAsync(model, new("unused", sink) { singleFile = singleFile, additionalUsings = ["System.Text", "System.Text"] }, CancellationToken.None);
        SyntaxTree[] trees = sink.sources.Select(source => CSharpSyntaxTree.ParseText(source.Value, path: source.Key))
            .Append(CSharpSyntaxTree.ParseText("namespace Consumer { public struct Pair<T> where T : unmanaged { public T X; public T Y; } }")).ToArray();
        string[] paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        CSharpCompilation compilation = CSharpCompilation.Create("ExternalWithRecipes" + Guid.NewGuid().ToString("N"), trees,
            paths.Append(typeof(BGCS.Runtime.NativeModuleSession).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path)),
            new(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, generalDiagnosticOption: ReportDiagnostic.Error));
        using MemoryStream output = new();
        var emitted = compilation.Emit(output);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        Assembly assembly = Assembly.Load(output.ToArray());
        Type conversions = assembly.GetType("Generated.ExternalHelpers.Api+RecipeConversions", true)!;
        Assert.Equal(7f, conversions.GetMethod("ToNative_vector")!.Invoke(null, [new Vector2(7, 9)]));
        Assert.Equal(new Vector2(11, 0), conversions.GetMethod("FromNative_vector")!.Invoke(null, [11f]));
    }

    private sealed class NamespaceSink : BGCS.Contracts.Artifacts.IArtifactSink
    {
        internal Dictionary<string, string> sources { get; } = new(StringComparer.Ordinal);
        public string WriteText(string relativePath, string content) { sources.Add(relativePath, content); return relativePath; }
    }

    [Fact]
    public void FriendlyCopiesUseExactFieldIdentitiesAndDoNotRequireMatchingClrOffsets()
    {
        (InteropModule module, ExternalTypeProjection projection) = Create();
        projection = projection with { managedStorage = projection.managedStorage! with { size = 16,
            fields = [projection.managedStorage.fields[0] with { offset = 8 }, projection.managedStorage.fields[1] with { offset = 0 }] } };
        CSharpExternalTypeProjectionPlan planned = new ExternalCarrierPlanner().Plan(module, projection, "RawPoint",
            new Dictionary<StableId, string> { [new("x")] = "nativeX", [new("y")] = "renamedY" });
        Assert.Equal("global::Consumer.Pair<float>", planned.managedType);
        Assert.Equal("RawPoint", planned.rawCarrierType);
        Assert.Equal("nativeX", planned.fields[0].rawField);
        Assert.Equal("Y", planned.fields[1].managedField);
        Assert.Throws<NotSupportedException>(() => new ExternalTypeContractValidator().ValidateNativeStorage(module, projection));
    }

    [Theory]
    [InlineData("missing-field")]
    [InlineData("duplicate-source")]
    [InlineData("duplicate-target")]
    [InlineData("different-sign")]
    [InlineData("same-size-different-kind")]
    [InlineData("private-field")]
    [InlineData("readonly-field")]
    [InlineData("overlapping-managed-fields")]
    [InlineData("asserted-evidence")]
    [InlineData("reference-containing")]
    [InlineData("union")]
    [InlineData("bitfield")]
    public void InvalidOrUnprovenFieldCopiesAreRejected(string mutation)
    {
        (InteropModule module, ExternalTypeProjection projection) = Create();
        ManagedTypeStorageFact fact = projection.managedStorage!;
        switch (mutation)
        {
            case "missing-field": projection = projection with { fields = [projection.fields[0]] }; break;
            case "duplicate-source": projection = projection with { fields = [projection.fields[0], projection.fields[1] with { nativeField = projection.fields[0].nativeField }] }; break;
            case "duplicate-target": projection = projection with { fields = [projection.fields[0], projection.fields[1] with { managedField = "X" }] }; break;
            case "different-sign": fact = fact with { fields = [fact.fields[0] with { scalarKind = ManagedScalarKind.UnsignedInteger, typeIdentity = "System.UInt32" }, fact.fields[1]] }; break;
            case "same-size-different-kind": fact = fact with { fields = [fact.fields[0] with { scalarKind = ManagedScalarKind.SignedInteger, typeIdentity = "System.Int32" }, fact.fields[1]] }; break;
            case "private-field": fact = fact with { fields = [fact.fields[0] with { isPublic = false }, fact.fields[1]] }; break;
            case "readonly-field": fact = fact with { fields = [fact.fields[0] with { isWritable = false }, fact.fields[1]] }; break;
            case "overlapping-managed-fields": fact = fact with { fields = [fact.fields[0], fact.fields[1] with { offset = 0 }] }; break;
            case "asserted-evidence": fact = fact with { evidence = new(EvidenceLevel.UserAsserted, "user") }; break;
            case "reference-containing": fact = fact with { isReferenceFree = false }; break;
            case "union": module = WithRecord(module, module.types[1] with { recordKind = "Union" }); break;
            case "bitfield": module = WithRecord(module, module.types[1] with { fields = [module.types[1].fields[0] with { bitWidth = 7 }, module.types[1].fields[1]] }); break;
        }
        projection = projection with { managedStorage = fact };
        Assert.ThrowsAny<Exception>(() => new ExternalTypeContractValidator().ValidateProjection(module, projection));
    }

    [Theory]
    [InlineData("System.Numerics.Vector2", "global::System.Numerics.Vector2")]
    [InlineData("Consumer.Pair<float>", "global::Consumer.Pair<float>")]
    [InlineData("global::Consumer.Pair<System.Int32>", "global::Consumer.Pair<System.Int32>")]
    public void ClosedConsumerTypesHaveStableUnambiguousSyntax(string input, string expected)
        => Assert.Equal(expected, ExternalCarrierPlanner.NormalizeType(input));

    [Theory]
    [InlineData("Consumer.Pair<>")]
    [InlineData("Consumer.Pair<int>[]")]
    [InlineData("int*")]
    [InlineData("dynamic")]
    [InlineData("(int, int)")]
    [InlineData("System.Int32?")]
    [InlineData("Consumer.Point; public class Injected {}")]
    public void UnsupportedOrInjectedTypeSyntaxIsRejected(string type)
        => Assert.Throws<ArgumentException>(() => ExternalCarrierPlanner.NormalizeType(type));

    [Fact]
    public void MatchingStorageDoesNotProduceAnyCallingAbiPermission()
    {
        (InteropModule module, ExternalTypeProjection projection) = Create();
        new ExternalTypeContractValidator().ValidateNativeStorage(module, projection);
        Assert.Equal(EvidenceLevel.Verified, projection.managedStorage!.evidence.level);
        Assert.Equal("bgcs:managed-field-probe:v1", projection.managedStorage.evidence.origin);
        // This contract contains no import convention or runtime aggregate-call certificate.
        Assert.DoesNotContain(typeof(ManagedTypeStorageFact).GetProperties(), property => property.Name.Contains("Calling", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(BindingImportMode.FunctionTable)]
    [InlineData(BindingImportMode.DllImport)]
    [InlineData(BindingImportMode.LibraryImport)]
    public async Task MatchingExternalStorageCannotBypassCallingProofButPointerShimsRemainCompilable(BindingImportMode mode)
    {
        (InteropModule original, ExternalTypeProjection projection) = Create();
        new ExternalTypeContractValidator().ValidateNativeStorage(original, projection);
        InteropFunction byValue = new(new("echo"), "echo", projection.nativeType,
            [new(new("value"), "value", projection.nativeType)], "C");
        InteropModule nativeValue = new(original.libraryId, original.targetId, original.pointerSize, original.types, [byValue], [])
        { byteOrder = original.byteOrder };
        ManagedApiModel valueModel = new DefaultManagedProjection().Project(nativeValue, new("Generated.ExternalPolicy", "Api"), CancellationToken.None)
            with { externalTypeProjections = [projection] };
        NeutralBackendTests.MemorySink rejected = new();
        CSharpPlanningException failure = await Assert.ThrowsAsync<CSharpPlanningException>(async () =>
            await new CSharpBackend().EmitAsync(valueModel, new("external", rejected) { importMode = mode }, CancellationToken.None));
        Assert.Contains("calling-ABI evidence", failure.Message, StringComparison.Ordinal);
        Assert.Contains("pointer bridge", failure.Message, StringComparison.Ordinal);
        Assert.Empty(rejected.sources);
        Assert.Equal(EvidenceLevel.Verified, projection.managedStorage!.evidence.level);

        InteropType pointer = new(new("point-pointer"), "Point*", NativeTypeCategory.Pointer, 8, 8) { elementType = projection.nativeType };
        InteropType nothing = new(new("void"), "void", NativeTypeCategory.Primitive, 0, 1) { primitiveKind = "Void" };
        InteropModule pointerModule = new(original.libraryId, original.targetId, original.pointerSize, original.types.Add(pointer).Add(nothing),
            [new(new("read"), "read", original.types[0].id, [new(new("read-value"), "value", pointer.id)], "C"),
                new(new("copy"), "copy", nothing.id, [new(new("copy-input"), "input", pointer.id), new(new("copy-output"), "output", pointer.id)], "C")], [])
        { byteOrder = original.byteOrder };
        ManagedApiModel pointerModel = new DefaultManagedProjection().Project(pointerModule, new("Generated.ExternalPolicy", "Api"), CancellationToken.None)
            with { externalTypeProjections = [projection] };
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("external", sink) { importMode = mode };
        CSharpEmissionPlan plan = new CSharpEmissionPlanner().Plan(pointerModel, request);
        Assert.Empty(plan.aggregateCalls);
        Assert.Single(plan.externalTypeProjections);
        Assert.All(plan.methods, method => Assert.All(method.parameterTypes, type => Assert.Equal("Point*", type)));
        Assert.All(plan.methods, method => Assert.Empty(method.valueProjections));
        await new CSharpBackend().EmitAsync(pointerModel, request, CancellationToken.None);
        new BGCS.CSharp.Validation.EmittedInteropContractReader().Verify(plan, sink.sources.Select(source => (source.Key, source.Value)), "external");
        const string consumer = """
            namespace Consumer { public struct Pair<T> where T : unmanaged { public T X; public T Y; } }
            namespace Generated.ExternalPolicy {
                public static unsafe class ConsumerProbe {
                    public static float Copy(Api api,Point* source,Point* result) { api.Raw.Copy(source,result); return api.Raw.Read(result); }
                }
            }
            """;
        await ValueReceiverTests.CompileWithSdkAsync(sink, consumer, sourceRuntime: false);
    }

    [Theory]
    [InlineData(NativeParameterDirection.Input)]
    [InlineData(NativeParameterDirection.Output)]
    [InlineData(NativeParameterDirection.InputOutput)]
    public async Task ExternalFieldCopiesDoNotClaimOriginalReferenceAliasing(NativeParameterDirection direction)
    {
        (InteropModule original, ExternalTypeProjection projection) = Create();
        Evidence evidence = new(EvidenceLevel.UserAsserted, "fixture:borrowed-value");
        InteropType pointer = new(new("point-pointer"), "Point*", NativeTypeCategory.Pointer, 8, 8) { elementType = projection.nativeType };
        InteropParameter parameter = new(new("parameter"), "point", pointer.id)
        {
            singleValue = new(direction, new(NativeLifetimeKind.Call, null, evidence), evidence) { retainsPointer = false, allowsNull = false },
            source = new("point.h", 3, 17)
        };
        InteropFunction function = new(new("change"), "change", original.types[0].id, [parameter], "C");
        InteropModule module = new(original.libraryId, original.targetId, original.pointerSize, original.types.Add(pointer), [function], [])
        { byteOrder = original.byteOrder };
        ManagedApiModel model = new DefaultManagedProjection().Project(module, new("Generated.ExternalBorrow", "Api"), CancellationToken.None)
            with { externalTypeProjections = [projection] };
        NamespaceSink sink = new();
        EmissionRequest request = new("external", sink);
        CSharpEmissionPlan plan = new CSharpEmissionPlanner().Plan(model, request);
        CSharpValueParameterOmissionPlan omitted = Assert.Single(plan.valueParameterOmissions);
        Assert.Equal(parameter.id, omitted.parameter);
        Assert.Equal(parameter.source, omitted.source);
        Assert.Contains("overlapping aliases", omitted.reason);
        CSharpMethodPlan method = Assert.Single(plan.methods);
        Assert.Empty(method.valueParameters);
        Assert.Empty(method.valueProjections);
        Assert.Equal("Point*", Assert.Single(method.parameterTypes));
        await new CSharpBackend().EmitAsync(model, request, CancellationToken.None);
        new BGCS.CSharp.Validation.EmittedInteropContractReader().Verify(plan, sink.sources.Select(source => (source.Key, source.Value)), "external");
        var raw = Assert.Single(sink.sources.Values.SelectMany(source => CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()), method => method.Identifier.ValueText == "Change");
        Assert.Equal("RawApi", ((Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax)raw.Parent!).Identifier.ValueText);
    }

    private static InteropModule WithRecord(InteropModule module, InteropType record)
        => new(module.libraryId, module.targetId, module.pointerSize, [module.types[0], record], [], []) { byteOrder = module.byteOrder };

    private static (InteropModule, ExternalTypeProjection) Create()
    {
        InteropType scalar = new(new("float"), "float", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Float" };
        InteropType record = new(new("point"), "Point", NativeTypeCategory.Record, 8, 4)
        { recordKind = "Struct", fields = [new(new("x"), "x", scalar.id, 0, 0, null), new(new("y"), "y", scalar.id, 4, 32, null)] };
        InteropModule module = new("external", "windows-x64-msvc", 8, [scalar, record], [], []) { byteOrder = NativeByteOrder.LittleEndian };
        ManagedProbeIdentity identity = new(".NET 10.0.12", "win-x64", "X64", 8, true, new(new string('1', 64)));
        ManagedTypeStorageFact storage = new("Consumer.Pair<float>", "Consumer.Pair`1[[System.Single]]", 8, 4, true, "Sequential",
            [new("X", "System.Single", ManagedScalarKind.BinaryFloat, 4, 0, true, true), new("Y", "System.Single", ManagedScalarKind.BinaryFloat, 4, 4, true, true)],
            identity, new(EvidenceLevel.Verified, "bgcs:managed-field-probe:v1"));
        return (module, new(record.id, storage.managedType, [new(new("x"), "X"), new(new("y"), "Y")]) { managedStorage = storage });
    }
}
