using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core;
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

public sealed class LegacyConfigurationStorageParityTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable)]
    [InlineData(BindingImportMode.DllImport)]
    [InlineData(BindingImportMode.LibraryImport)]
    public async Task ScalarAliasChainsKeepNativeIdentityAndCanonicalRawSignatures(BindingImportMode mode)
    {
        InteropType integer = Integer();
        InteropType first = new(new("base-int"), "BaseInt", NativeTypeCategory.Alias, 4, 4) { elementType = integer.id };
        InteropType second = new(new("alias-int"), "AliasInt", NativeTypeCategory.Alias, 4, 4) { elementType = first.id };
        InteropFunction function = new(new("alias-add"), "alias_add", second.id, [new(new("input"), "value", second.id)], "C");
        InteropModule native = new("scalar_alias", "windows-x64-msvc", 8, [integer, first, second], [function], []) { byteOrder = NativeByteOrder.LittleEndian };
        ManagedApiModel model = new DefaultManagedProjection().Project(native, new("Generated.ResidualStorage", "AliasApi"), CancellationToken.None);
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("scalar_alias", sink) { importMode = mode };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        CSharpMethodPlan method = Assert.Single(plan.methods);
        Assert.Equal("int", method.returnType);
        Assert.Equal("int", Assert.Single(method.parameterTypes));
        Assert.Equal(function.id, method.nativeExport);
        Assert.Same(native, model.interop);
        Assert.Equal(first.id, native.GetType(second.id).elementType);
        Assert.Equal(integer.id, native.GetType(first.id).elementType);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        new EmittedInteropContractReader().Verify(plan, sink.sources.Select(source => (source.Key, source.Value)), request.libraryName);
        Assert.DoesNotContain(sink.sources.Keys, path => path.EndsWith("/AliasInt.g.cs", StringComparison.Ordinal) || path.EndsWith("/BaseInt.g.cs", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitFieldNamesArrayStorageAndConstructorCopiesComposeWithoutChangingOffsets(bool singleFile)
    {
        InteropType integer = Integer();
        InteropType octet = new(new("byte"), "unsigned char", NativeTypeCategory.Primitive, 1, 1) { primitiveKind = "UnsignedChar", isSigned = false };
        InteropType array = new(new("url-bytes"), "unsigned char[4]", NativeTypeCategory.Array, 4, 1) { elementType = octet.id, arrayLength = 4 };
        InteropType record = new(new("sample"), "sample_type", NativeTypeCategory.Record, 12, 4)
        {
            declarationName = "sample_type",
            recordKind = "Struct",
            fields = [new(new("id-field"), "id", integer.id, 0, 0, null), new(new("url-field"), "url", array.id, 4, 32, null), new(new("cpu-field"), "cpu", integer.id, 8, 64, null)]
        };
        InteropModule native = new("record_copy", "windows-x64-msvc", 8, [integer, octet, array, record], [], []) { byteOrder = NativeByteOrder.LittleEndian };
        ManagedProjectionRequest projection = new("Generated.ResidualStorage", "RecordApi")
        {
            names = ImmutableDictionary<string, string>.Empty.Add(array.id.value, "UrlBytes"),
            records = [new("sample_type")
            {
                name = "SampleType", fields = [new("id") { name = "ID" }, new("url") { name = "URL" }, new("cpu") { name = "CPU" }],
                constructorFields = ["id", "url", "cpu"], emitSizeConstant = true
            }]
        };
        ManagedApiModel model = new DefaultManagedProjection().Project(native, projection, CancellationToken.None);
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("record_copy", sink) { singleFile = singleFile };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        CSharpStoragePlan storage = Assert.Single(plan.storage, item => item.nativeType == record.id);
        Assert.Equal(new[] { "ID", "URL", "CPU" }, storage.presentation!.constructorFields.Select(field => field.name));
        Assert.Equal(new[] { 0, 4, 8 }, storage.fields.Select(field => field.offset));
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        new EmittedInteropContractReader().Verify(plan, sink.sources.Select(source => (source.Key, source.Value)), request.libraryName);
        const string consumer = """
            using System;
            using System.Runtime.InteropServices;
            namespace Generated.ResidualStorage;
            public static class Probe {
                public static int Run() {
                    UrlBytes bytes = default;
                    bytes[0] = 7;
                    bytes[3] = 9;
                    SampleType sample = new(41, bytes, 2);
                    bytes[0] = 99;
                    if (sample.ID != 41 || sample.CPU != 2 || sample.URL[0] != 7 || sample.URL[3] != 9) return 1;
                    Span<byte> view = sample.URL;
                    view[3] = 12;
                    if (sample.URL[3] != 12 || view.Length != 4 || SampleType.NativeSize != 12 || Marshal.SizeOf<SampleType>() != 12) return 2;
                    if (Marshal.OffsetOf<SampleType>("ID").ToInt32() != 0 || Marshal.OffsetOf<SampleType>("URL").ToInt32() != 4 || Marshal.OffsetOf<SampleType>("CPU").ToInt32() != 8) return 3;
                    return 42;
                }
            }
            """;
        CSharpCompilation compilation = CSharpCompilation.Create("ResidualStorage_" + Guid.NewGuid().ToString("N"),
            sink.sources.Select(source => CSharpSyntaxTree.ParseText(source.Value, new(LanguageVersion.CSharp14), source.Key))
                .Append(CSharpSyntaxTree.ParseText(consumer, new(LanguageVersion.CSharp14))),
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                .Append(typeof(BGCS.Runtime.NativeModuleSession).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path)),
            new(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error));
        using MemoryStream image = new();
        var emitted = compilation.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        image.Position = 0;
        AssemblyLoadContext context = new("BGCS.ResidualStorage.Test", isCollectible: true);
        try
        {
            Assembly assembly = context.LoadFromStream(image);
            Assert.Equal(42, assembly.GetType("Generated.ResidualStorage.Probe", true)!.GetMethod("Run")!.Invoke(null, null));
        }
        finally { context.Unload(); }
    }

    [Fact]
    public void AGlobalFourByteCarrierCannotReplaceCompilerProvenOneByteBooleanStorage()
    {
        InteropType boolean = new(new("bool"), "_Bool", NativeTypeCategory.Primitive, 1, 1) { primitiveKind = "Bool", isSigned = false };
        InteropFunction function = new(new("echo"), "echo", boolean.id, [new(new("value"), "value", boolean.id)], "C");
        InteropModule native = new("bool_policy", "windows-x64-msvc", 8, [boolean], [function], []) { byteOrder = NativeByteOrder.LittleEndian };
        ManagedApiModel original = new DefaultManagedProjection().Project(native, new("Generated.ResidualStorage", "BooleanApi"), CancellationToken.None);
        NeutralBackendTests.MemorySink sink = new();
        CSharpEmissionPlan valid = new CSharpTestPlanner().Plan(original, new("bool_policy", sink));
        Assert.Equal("byte", Assert.Single(valid.methods).returnType);
        Assert.Equal("byte", Assert.Single(valid.methods).parameterTypes[0]);
        ManagedApiModel invalid = original with { types = original.types.Select(type => type.nativeType == boolean.id ? type with { frameworkType = "System.Int32" } : type).ToImmutableArray() };
        Assert.ThrowsAny<NotSupportedException>(() => new CSharpTestPlanner().Plan(invalid, new("bool_policy", sink)));
        Assert.Empty(sink.sources);
    }

    private static InteropType Integer() => new(new("int"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
}
