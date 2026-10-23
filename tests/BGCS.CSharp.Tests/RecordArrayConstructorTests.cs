using System;
using System.Collections.Immutable;
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

public sealed class RecordArrayConstructorTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task FixedPrimitiveArraysKeepRawConstructorsAndAddIndependentExactSpanCopies(
        BindingImportMode mode,
        bool sourceRuntime
    ) {
        ManagedApiModel model = Project(Module());
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native", sink)
        {
            importMode = mode,
            includeRuntimeSource = sourceRuntime,
            runtimeNamespace = sourceRuntime ? "Generated.ArrayConstructors.Runtime" : "BGCS.Runtime",
            singleFile = sourceRuntime
        };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        CSharpStoragePlan record = Assert.Single(plan.storage, carrier => carrier.nativeType == new StableId("record"));
        Assert.Equal(new[] { 1, 3 }, record.presentation!.arrayCopies.Select(copy => copy.length));
        Assert.Empty(record.presentation.arrayCopyOmissions);
        Assert.Equal(new[] { "byte", "int" }, record.presentation.arrayCopies.Select(copy => copy.elementType));
        string bytes = plan.storage.Single(carrier => carrier.nativeType == new StableId("bytes")).name;
        string values = plan.storage.Single(carrier => carrier.nativeType == new StableId("values")).name;
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        await ValueReceiverTests.CompileWithSdkAsync(sink, $$"""
            using System;
            using System.Runtime.InteropServices;
            namespace Generated.ArrayConstructors;
            public static class ArrayConstructorConsumer {
                public static int Run() {
                    byte[] data=[42];int[] values=[1,2,3];
                    Packet copied=new(data,values);
                    data[0]=99;values[1]=99;
                    if(copied.data[0]!=42 || copied.values[1]!=2 || Marshal.SizeOf<Packet>()!=16)return 1;
                    {{bytes}} rawBytes=default;rawBytes[0]=7;
                    {{values}} rawValues=default;rawValues[2]=8;
                    Packet raw=new(rawBytes,rawValues);
                    rawBytes[0]=9;rawValues[2]=9;
                    if(raw.data[0]!=7 || raw.values[2]!=8)return 2;
                    bool rejected=false;try{_=new Packet(Array.Empty<byte>(),new int[3]);}catch(ArgumentException){rejected=true;}
                    if(!rejected)return 3;
                    rejected=false;try{_=new Packet(new byte[1],new int[2]);}catch(ArgumentException){rejected=true;}
                    if(!rejected)return 4;
                    rejected=false;try{_=new Packet(new byte[1],new int[4]);}catch(ArgumentException){rejected=true;}
                    return rejected?42:5;
                }
            }
            """, sourceRuntime);
        var sources = sink.sources.Select(pair => (Path: pair.Key, Text: pair.Value)).ToArray();
        new EmittedInteropContractReader().Verify(plan, sources, "native");
        foreach (string mutation in new[] { "extent", "copy", "loop", "raw-constructor", "span-constructor" })
        {
            int edits = 0;
            var changed = sources.Select(source =>
            {
                SyntaxNode root = CSharpSyntaxTree.ParseText(source.Text).GetRoot();
                StructDeclarationSyntax? packet = root.DescendantNodes().OfType<StructDeclarationSyntax>().SingleOrDefault(type => type.Identifier.ValueText == "Packet");
                if (packet is null) return source;
                ConstructorDeclarationSyntax constructor = packet.Members.OfType<ConstructorDeclarationSyntax>().Single(item =>
                    item.ParameterList.Parameters.Any(parameter => parameter.Type!.ToString().Contains("ReadOnlySpan", StringComparison.Ordinal)) == (mutation != "raw-constructor"));
                ConstructorDeclarationSyntax? altered = constructor;
                if (mutation.EndsWith("constructor", StringComparison.Ordinal)) altered = null;
                else if (mutation == "copy")
                {
                    AssignmentExpressionSyntax assignment = constructor.DescendantNodes().OfType<AssignmentExpressionSyntax>().First(item => item.Left is ElementAccessExpressionSyntax);
                    altered = constructor.ReplaceNode(assignment, assignment.WithRight(SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0))));
                }
                else
                {
                    BinaryExpressionSyntax condition = constructor.DescendantNodes().OfType<BinaryExpressionSyntax>().First(item => item.IsKind(mutation == "extent" ? SyntaxKind.NotEqualsExpression : SyntaxKind.LessThanExpression));
                    altered = constructor.ReplaceNode(condition, condition.WithRight(SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(2))));
                }
                edits++;
                SyntaxNode changedRoot = altered is null ? root.RemoveNode(constructor, SyntaxRemoveOptions.KeepNoTrivia)! : root.ReplaceNode(constructor, altered);
                return (source.Path, Text: changedRoot.ToFullString());
            }).ToArray();
            Assert.Equal(1, edits);
            Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan, changed, "native"));
        }
    }

    [Theory]
    [InlineData("pointer")]
    [InlineData("enum")]
    [InlineData("record")]
    [InlineData("boolean")]
    public async Task UnsupportedSpanElementsKeepTheExactRawArrayConstructorAndExplainTheRestriction(string shape)
    {
        InteropType integer = new(new("integer"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropType element = shape switch
        {
            "pointer" => new(new("element"), "int*", NativeTypeCategory.Pointer, 8, 8) { elementType = integer.id },
            "enum" => new(new("element"), "Mode", NativeTypeCategory.Enumeration, 4, 4)
            {
                declarationName = "Mode",
                elementType = integer.id,
                enumValues = [new(new("member"), "Zero", "0", "0")]
            },
            "record" => new(new("element"), "Inner", NativeTypeCategory.Record, 4, 4) { declarationName = "Inner", recordKind = "Struct", fields = [new(new("value"), "value", integer.id, 0, 0, null)] },
            _ => new(new("element"), "_Bool", NativeTypeCategory.Primitive, 1, 1) { primitiveKind = "Bool", isSigned = false }
        };
        InteropType array = new(new("bytes"), "DataArray", NativeTypeCategory.Array, element.size, element.alignment) { elementType = element.id, arrayLength = 1 };
        InteropType record = new(new("record"), "Packet", NativeTypeCategory.Record, array.size, array.alignment) { declarationName = "Packet", recordKind = "Struct", fields = [new(new("data"), "data", array.id, 0, 0, null)] };
        InteropModule module = new("array-constructor", "windows-x64-msvc", 8, [integer, element, array, record], [], []) { byteOrder = NativeByteOrder.LittleEndian };
        ManagedApiModel model = Project(module, onlyData: true);
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native", sink);
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        CSharpRecordPresentationPlan presentation = plan.storage.Single(carrier => carrier.nativeType == record.id).presentation!;
        Assert.Empty(presentation.arrayCopies);
        Assert.Equal(new StableId("data"), Assert.Single(presentation.arrayCopyOmissions).nativeField);
        Assert.Contains("raw", presentation.arrayCopyOmissions[0].reason, StringComparison.OrdinalIgnoreCase);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        StructDeclarationSyntax generated = Assert.Single(sink.sources.SelectMany(source => CSharpSyntaxTree.ParseText(source.Value).GetRoot().DescendantNodes().OfType<StructDeclarationSyntax>()), type => type.Identifier.ValueText == "Packet");
        Assert.Single(generated.Members.OfType<ConstructorDeclarationSyntax>());
        Assert.DoesNotContain("ReadOnlySpan", generated.ToFullString(), StringComparison.Ordinal);
        new EmittedInteropContractReader().Verify(plan, sink.sources.Select(source => (source.Key, source.Value)), "native");
    }

    private static ManagedApiModel Project(
        InteropModule module,
        bool onlyData = false
    ) => new DefaultManagedProjection().Project(module,
        new("Generated.ArrayConstructors", "Api") { records = [new("Packet") { constructorFields = onlyData ? ["data"] : ["data", "values"] }] }, CancellationToken.None);

    private static InteropModule Module()
    {
        InteropType octet = new(new("octet"), "unsigned char", NativeTypeCategory.Primitive, 1, 1) { primitiveKind = "UnsignedChar", isSigned = false };
        InteropType integer = new(new("integer"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropType bytes = new(new("bytes"), "ByteArray", NativeTypeCategory.Array, 1, 1) { elementType = octet.id, arrayLength = 1 };
        InteropType alias = new(new("byte-alias"), "NativeBytes", NativeTypeCategory.Alias, 1, 1) { elementType = bytes.id };
        InteropType values = new(new("values"), "IntArray", NativeTypeCategory.Array, 12, 4) { elementType = integer.id, arrayLength = 3 };
        InteropType record = new(new("record"), "Packet", NativeTypeCategory.Record, 16, 4)
        {
            declarationName = "Packet",
            recordKind = "Struct",
            fields = [new(new("data"), "data", alias.id, 0, 0, null), new(new("array-values"), "values", values.id, 4, 32, null)]
        };
        return new("array-constructor", "windows-x64-msvc", 8, [octet, integer, bytes, alias, values, record], [], []) { byteOrder = NativeByteOrder.LittleEndian };
    }
}
