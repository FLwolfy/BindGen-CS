using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Analysis.Layout;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Managed;
using BGCS.Contracts.Native;
using BGCS.Contracts.Requests;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class StaticExternalProjectionBinderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FrozenSourceAndMetadataBindClosedFieldCopiesWithoutLayoutClaims(bool metadata)
    {
        const string source = "namespace Consumer { public struct Value<T> where T : unmanaged { public nint Address; public T Count; } }";
        ManagedApiModel original = Model();
        ManagedApiModel bound = StaticExternalProjectionBinder.Bind(original, Inputs(source, metadata), CancellationToken.None);
        Assert.Same(original.interop, bound.interop);
        ExternalTypeProjection projection = Assert.Single(bound.externalTypeProjections);
        Assert.Null(projection.managedStorage);
        Assert.Equal(EvidenceLevel.Declared, projection.managedShape!.evidence.level);
        Assert.Equal(ManagedScalarKind.NativeAddress, projection.managedShape.fields.Single(field => field.name == "Address").scalarKind);
        Assert.Equal(4, projection.managedShape.fields.Single(field => field.name == "Count").size);
        Assert.Throws<NotSupportedException>(() => new ExternalTypeContractValidator().ValidateNativeStorage(original.interop, projection));
        NeutralBackendTests.MemorySink sink = new();
        CSharpEmissionPlan plan = new CSharpEmissionPlanner().Plan(bound, new("native", sink));
        Assert.Equal("RawPacket", Assert.Single(plan.externalTypeProjections).rawCarrierType);
        await new CSharpBackend().EmitAsync(bound, new("native", sink), CancellationToken.None);
        string text = string.Join("\n", sink.sources.Values);
        Assert.Contains("result.Count = value.Count", text);
        Assert.Contains("Unsafe.WriteUnaligned<nint>", text);
        Assert.Contains("__ToExternalNative_", text);
    }

    [Theory]
    [InlineData("public readonly int Count; public nint Address;")]
    [InlineData("private int Count; public nint Address;")]
    [InlineData("public uint Count; public nint Address;")]
    [InlineData("public long Count; public nint Address;")]
    [InlineData("public int Count; public long Address;")]
    [InlineData("public int Count; public nint Address; public int Extra;")]
    [InlineData("public int Count; public nint Address; public string Extra;")]
    [InlineData("public int Count; public delegate* unmanaged<void> Address;")]
    public void UnsafeOrIncompleteScalarShapesAreRejected(string fields)
    {
        string source = "namespace Consumer { public unsafe struct Value<T> where T : unmanaged { " + fields + " } }";
        Assert.ThrowsAny<Exception>(() => StaticExternalProjectionBinder.Bind(Model(), Inputs(source, false), CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitLayoutIsRejectedFromSourceAndPseudoMetadata(bool metadata)
    {
        const string source = "namespace Consumer { [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)] public struct ExplicitValue { [System.Runtime.InteropServices.FieldOffset(0)] public int Count; [System.Runtime.InteropServices.FieldOffset(8)] public nint Address; } }";
        ManagedApiModel model = Model();
        model = model with { externalTypeProjections = [model.externalTypeProjections[0] with { managedType = "Consumer.ExplicitValue" }] };
        Assert.ThrowsAny<Exception>(() => StaticExternalProjectionBinder.Bind(model, Inputs(source, metadata), CancellationToken.None));
    }

    [Fact]
    public void MissingMetadataAndChangedFrozenBytesCannotAcquireStaticFacts()
    {
        Assert.ThrowsAny<Exception>(() => StaticExternalProjectionBinder.Bind(Model(), null, CancellationToken.None));
        ManagedProjectionInputs input = Inputs("namespace Consumer { public struct Value<T> where T:unmanaged { public int Count; public nint Address; } }", false);
        Assert.ThrowsAny<Exception>(() => StaticExternalProjectionBinder.Bind(Model(), input with
        { sources = [input.sources[0] with { digest = new(new string('0', 64)) }] }, CancellationToken.None));
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => StaticExternalProjectionBinder.Bind(Model(), input, cancelled.Token));
    }

    private static ManagedApiModel Model()
    {
        InteropType integer = new(new("int"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropType address = new(new("pointer"), "int*", NativeTypeCategory.Pointer, 8, 8) { elementType = integer.id };
        InteropType record = new(new("record"), "RawPacket", NativeTypeCategory.Record, 16, 8)
        { recordKind = "Struct", fields = [new(new("count"), "Count", integer.id, 0, 0, null), new(new("address"), "Address", address.id, 8, 64, null)] };
        InteropModule module = new("static-copy", "windows-x64-msvc", 8, [integer, address, record], [], []) { byteOrder = NativeByteOrder.LittleEndian };
        return new DefaultManagedProjection().Project(module, new("Generated", "Api"), CancellationToken.None) with
        { externalTypeProjections = [new(record.id, "Consumer.Value<int>", [new(new("count"), "Count"), new(new("address"), "Address")])] };
    }

    private static ManagedProjectionInputs Inputs(string source, bool metadata)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(source);
        if (!metadata) return new() { sources = [new(Path.Combine(Path.GetTempPath(), "bgcs-static-copy.cs"),
            new(Convert.ToHexStringLower(SHA256.HashData(bytes))), bytes.ToImmutableArray(), source)] };
        string framework = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(path => Path.GetDirectoryName(path) == framework).Select(path => MetadataReference.CreateFromFile(path));
        CSharpCompilation compilation = CSharpCompilation.Create("StaticCopyFixture", [CSharpSyntaxTree.ParseText(source)], references,
            new(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        using MemoryStream output = new();
        var emitted = compilation.Emit(output);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        bytes = output.ToArray();
        return new() { assemblies = [new(Path.Combine(Path.GetTempPath(), "StaticCopyFixture.dll"), compilation.Assembly.Identity.ToString(),
            new(Convert.ToHexStringLower(SHA256.HashData(bytes))), bytes.ToImmutableArray(), false)] };
    }
}
