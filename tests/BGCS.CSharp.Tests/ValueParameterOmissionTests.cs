using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Native;
using BGCS.Contracts.Requests;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using BGCS.CSharp.Validation;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class ValueParameterOmissionTests
{
    [Theory]
    [InlineData("CppDto")]
    [InlineData("Opaque")]
    [InlineData("Struct")]
    public async Task UnprovenManagedTemporaryStorageOmitsOnlyConvenienceAndRetainsExactRawAbi(string recordKind)
    {
        Evidence evidence = new(EvidenceLevel.UserAsserted, "fixture:explicit-value");
        InteropType integer = new(new("integer"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropType record = new(new("record"), "Record", NativeTypeCategory.Record, 4, 4)
        {
            declarationName = "Record", recordKind = recordKind,
            fields = [new(new("field"), "value", integer.id, 0, recordKind == "Struct" ? 0 : null, recordKind == "Struct" ? 8 : null)]
        };
        InteropType pointer = new(new("pointer"), "Record*", NativeTypeCategory.Pointer, 8, 8) { elementType = record.id };
        InteropParameter parameter = new(new("parameter"), "value", pointer.id)
        {
            singleValue = new(NativeParameterDirection.InputOutput, new(NativeLifetimeKind.Call, null, evidence), evidence)
                { retainsPointer = false, allowsNull = false }, source = new("fixture.h", 3, 12)
        };
        InteropFunction function = new(new("change"), "change", integer.id, [parameter], "C");
        InteropModule module = new("omission", "windows-x64-msvc", 8, [integer, record, pointer], [function], []) { byteOrder = NativeByteOrder.LittleEndian };
        var model = new DefaultManagedProjection().Project(module, new("Generated.Omission", "Api"), CancellationToken.None);
        Assert.Equal(parameter.id, Assert.Single(Assert.Single(model.methods).valuePointers));
        Sink sink = new();
        EmissionRequest request = new("fixture", sink);
        CSharpEmissionPlan plan = new CSharpEmissionPlanner().Plan(model, request);
        CSharpValueParameterOmissionPlan omission = Assert.Single(plan.valueParameterOmissions);
        Assert.Equal(parameter.id, omission.parameter);
        Assert.Equal(function.id, omission.nativeExport);
        Assert.Equal(parameter.source, omission.source);
        Assert.Contains("verified generated raw storage and managed alignment", omission.reason);
        Assert.Empty(Assert.Single(plan.methods).valueParameters);
        Assert.Equal("Record*", Assert.Single(Assert.Single(plan.methods).parameterTypes));
        await new CSharpBackend().EmitAsync(model, request, CancellationToken.None);
        new EmittedInteropContractReader().Verify(plan, sink.contents.Select(pair => (pair.Key, pair.Value)), "fixture");
        var roots = sink.contents.Values.Select(source => CSharpSyntaxTree.ParseText(source).GetRoot()).ToArray();
        MethodDeclarationSyntax raw = Assert.Single(roots.SelectMany(root => root.DescendantNodes().OfType<MethodDeclarationSyntax>()),
            method => method.Identifier.ValueText == "Change");
        Assert.Equal("RawApi", ((ClassDeclarationSyntax)raw.Parent!).Identifier.ValueText);
        Assert.IsType<PointerTypeSyntax>(Assert.Single(raw.ParameterList.Parameters).Type);
    }

    private sealed class Sink : IArtifactSink
    {
        internal Dictionary<string, string> contents { get; } = new(StringComparer.Ordinal);
        public string WriteText(string relativePath, string content) { contents.Add(relativePath, content); return Path.GetFullPath(relativePath); }
    }
}
