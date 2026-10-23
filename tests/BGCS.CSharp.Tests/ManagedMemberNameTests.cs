using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using BGCS.Core.Artifacts;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class ManagedMemberNameTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QualifiedMemberMappingsCompileAndPreserveNativeStorageAndFunctionIdentities(bool singleFile)
    {
        InteropModule native = Module();
        ManagedProjectionRequest request = new("Generated.Names", "Api")
        {
            names = ImmutableDictionary<string, string>.Empty.Add("Value::first", "event").Add("Value::bits", "Flags")
                .Add("Kind::READY", "Ready").Add("read::native_input", "managedInput")
        };
        ManagedApiModel model = new DefaultManagedProjection().Project(native, request, CancellationToken.None);
        model = JsonSerializer.Deserialize<ManagedApiModel>(JsonSerializer.SerializeToUtf8Bytes(model))!;
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, new("unused", new Sink()));
        Assert.Equal(native.functions[0].id, Assert.Single(plan.methods).nativeExport);
        Assert.Equal("read", plan.methods[0].exportName);
        Assert.Equal("managedInput", Assert.Single(plan.methods[0].parameterNames));
        Sink sink = new();
        await new CSharpTestBackend().EmitAsync(model, new("unused", sink) { singleFile = singleFile }, CancellationToken.None);
        string trusted = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        CSharpCompilation compilation = CSharpCompilation.Create("MappedMembers" + Guid.NewGuid().ToString("N"),
            sink.sources.Select(source => CSharpSyntaxTree.ParseText(source.Value, path: source.Key)),
            trusted.Split(Path.PathSeparator).Append(typeof(BGCS.Runtime.NativeModuleSession).Assembly.Location)
                .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path)),
            new(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, generalDiagnosticOption: ReportDiagnostic.Error));
        using MemoryStream output = new();
        var emitted = compilation.Emit(output);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        Assembly assembly = Assembly.Load(output.ToArray());
        Type storage = assembly.GetType("Generated.Names.Value", true)!;
        Assert.Equal(8, Marshal.SizeOf(storage));
        Assert.Equal(0, Marshal.OffsetOf(storage, "event").ToInt32());
        Assert.Null(storage.GetField("first"));
        Assert.NotNull(storage.GetProperty("Flags"));
        Type enumeration = assembly.GetType("Generated.Names.Kind", true)!;
        Assert.Equal(["Ready"], Enum.GetNames(enumeration));
        Assert.Equal(1, Convert.ToInt32(Enum.Parse(enumeration, "Ready")));
        foreach (string type in new[] { "Generated.Names.Api", "Generated.Names.Api+RawApi" })
        {
            MethodInfo method = assembly.GetType(type, true)!.GetMethod("Read")!;
            Assert.Equal("managedInput", Assert.Single(method.GetParameters()).Name);
            Assert.Equal(typeof(int), method.ReturnType);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidMemberMappingsFailBeforeWritingAnyCandidate(bool unknownIdentity)
    {
        ManagedApiModel model = new DefaultManagedProjection().Project(Module(), new("Generated.Names", "Api"), CancellationToken.None);
        ManagedType record = model.types.Single(type => type.nativeType == new StableId("record"));
        record = record with { memberNames = unknownIdentity
            ? record.memberNames.Add(new(new("absent"), "NoSuchField"))
            : record.memberNames.Select(member => member with { name = "Same" }).ToImmutableArray() };
        model = model with { types = model.types.Select(type => type.nativeType == record.nativeType ? record : type).ToImmutableArray() };
        Sink sink = new();
        Assert.Throws<ArgumentException>(() => new CSharpTestPlanner().Plan(model, new("unused", sink)));
        Assert.Empty(sink.sources);
    }

    private static InteropModule Module()
    {
        InteropType integer = new(new("int"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropType record = new(new("record"), "Value", NativeTypeCategory.Record, 8, 4)
        {
            declarationName = "Value",
            fields = [new(new("first"), "first", integer.id, 0, 0, null), new(new("bits"), "bits", integer.id, 4, 32, 3)]
        };
        InteropType enumeration = new(new("enum"), "Kind", NativeTypeCategory.Enumeration, 4, 4)
        { declarationName = "Kind", elementType = integer.id, enumValues = [new(new("ready"), "READY", "1", "1")] };
        InteropFunction function = new(new("read"), "read", integer.id, [new(new("input"), "native_input", integer.id)], "C");
        return new("names", "windows-x64-msvc", 8, [integer, record, enumeration], [function], []) { byteOrder = NativeByteOrder.LittleEndian };
    }

    private sealed class Sink : IArtifactSink
    {
        internal Dictionary<string, string> sources { get; } = new(StringComparer.Ordinal);
        public string WriteText(string relativePath, string content) { sources.Add(relativePath, content); return relativePath; }
    }
}
