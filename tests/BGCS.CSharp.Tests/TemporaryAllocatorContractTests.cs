using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Managed;
using BGCS.Contracts.Native;
using BGCS.Contracts.Requests;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using BGCS.CSharp.Validation;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class TemporaryAllocatorContractTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task ProcessedTextCannotChangeItsExactSessionFactoryOrMatchingAllocation(BindingImportMode mode, bool shared)
    {
        InteropType octet = new(new("octet"), "char", NativeTypeCategory.Primitive, 1, 1) { primitiveKind = "Char", isSigned = true };
        InteropType pointer = new(new("text-pointer"), "const char*", NativeTypeCategory.Pointer, 8, 8) { elementType = octet.id };
        InteropType integer = new(new("integer"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        Evidence evidence = new(EvidenceLevel.UserAsserted, "fixture:explicit-nonretained-text");
        StringContract contract = new(NativeTextEncoding.Utf8, 1, NativeLengthUnit.CodeUnits, NativeParameterDirection.Input,
            new(NativeLifetimeKind.Call, null, evidence), evidence) { zeroTerminated = true, retainsPointer = false, allowsNull = false };
        InteropParameter parameter = new(new("text"), "text", pointer.id) { text = contract };
        InteropModule interop = new("temporary-allocator", "windows-x64-msvc", 8, [octet, pointer, integer],
            [new(new("text-size"), "text_size", integer.id, [parameter], "C")], []);
        ManagedApiModel model = new DefaultManagedProjection().Project(interop,
            new("Generated.Allocator", "AllocatorApi") { style = shared ? ManagedApiStyle.Static : ManagedApiStyle.Instance }, CancellationToken.None);
        Sink sink = new();
        EmissionRequest request = new("fixture", sink) { importMode = mode };
        CSharpEmissionPlan plan = new CSharpEmissionPlanner().Plan(model, request);
        await new CSharpBackend().EmitAsync(model, request, CancellationToken.None);
        var sources = sink.sources.Select(pair => (Path: pair.Key, Text: pair.Value)).ToArray();
        EmittedInteropContractReader reader = new();
        reader.Verify(plan, sources, request.libraryName);
        Assert.Contains(sources, source => source.Text.Contains("temporaryAllocatorFactory = null", StringComparison.Ordinal));
        foreach ((string original, string replacement) in new[]
        {
            ("temporaryAllocatorFactory(m_session)", "temporaryAllocatorFactory(null!)"),
            ("if (!m_temporaryAllocator.IsBoundTo(m_session))", "if (false)"),
            (", this.m_temporaryAllocator)", ", null)"),
            ("m_temporaryAllocator;", "m_temporaryAllocator = null;")
        })
        {
            Assert.Contains(sources, source => source.Text.Contains(original, StringComparison.Ordinal));
            InvalidOperationException rejected = Assert.Throws<InvalidOperationException>(() => reader.Verify(plan,
                sources.Select(source => (source.Path, source.Text.Replace(original, replacement, StringComparison.Ordinal))), request.libraryName));
            Assert.Contains("temporary", rejected.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData(BindingImportMode.FunctionTable)]
    [InlineData(BindingImportMode.DllImport)]
    [InlineData(BindingImportMode.LibraryImport)]
    public async Task OptionalAllocatorCannotHideTheOrdinaryParameterlessFactory(BindingImportMode mode)
    {
        InteropType octet = new(new("octet"), "char", NativeTypeCategory.Primitive, 1, 1) { primitiveKind = "Char", isSigned = true };
        InteropType pointer = new(new("pointer"), "const char*", NativeTypeCategory.Pointer, 8, 8) { elementType = octet.id };
        InteropType integer = new(new("integer"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        Evidence evidence = new(EvidenceLevel.UserAsserted, "fixture:text-and-native-factory-name");
        StringContract text = new(NativeTextEncoding.Utf8, 1, NativeLengthUnit.CodeUnits, NativeParameterDirection.Input,
            new(NativeLifetimeKind.Call, null, evidence), evidence) { zeroTerminated = true, retainsPointer = false, allowsNull = false };
        string factory = mode == BindingImportMode.FunctionTable ? "load" : "create";
        InteropModule interop = new("collision", "windows-x64-msvc", 8, [octet, pointer, integer],
            [new(new("read"), "read", integer.id, [new(new("text"), "text", pointer.id) { text = text }], "C"),
             new(new("factory"), factory, integer.id, [], "C")], []);
        ManagedApiModel model = new DefaultManagedProjection().Project(interop, new("Generated.Collision", "CollisionApi"), CancellationToken.None);
        Sink sink = new();
        InvalidOperationException rejected = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new CSharpBackend().EmitAsync(model, new("fixture", sink) { importMode = mode }, CancellationToken.None));
        Assert.Contains("factory", rejected.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(sink.sources);
    }

    private sealed class Sink : IArtifactSink
    {
        internal Dictionary<string, string> sources { get; } = new(StringComparer.Ordinal);
        public string WriteText(string relativePath, string content) { sources.Add(relativePath, content); return relativePath; }
    }
}
