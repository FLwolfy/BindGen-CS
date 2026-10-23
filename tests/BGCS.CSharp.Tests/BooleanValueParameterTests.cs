using System;
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
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class BooleanValueParameterTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task ExplicitNativeBooleanBorrowsKeepOriginalAddressesAndSeparateRawCarriers(BindingImportMode mode, bool sourceRuntime)
    {
        InteropModule module = Module();
        ManagedApiModel model = new DefaultManagedProjection().Project(module, new("Generated.BooleanValues", "BooleanApi")
        {
            style = sourceRuntime ? ManagedApiStyle.Static : ManagedApiStyle.Instance,
            functionAliases = [new("change", "ChangeAlias")],
            functionContainers = [new("Booleans", [new("change", "Apply")])]
        }, CancellationToken.None);
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native", sink) { importMode = mode, includeRuntimeSource = sourceRuntime, singleFile = sourceRuntime,
            runtimeNamespace = sourceRuntime ? "Generated.BooleanValues.Runtime" : "BGCS.Runtime" };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        CSharpMethodPlan method = Assert.Single(plan.methods);
        Assert.All(method.parameterTypes, type => Assert.Equal("byte*", type));
        Assert.Equal(new[] { NativeParameterDirection.Input, NativeParameterDirection.Output, NativeParameterDirection.InputOutput }, method.valueParameters.Select(value => value.direction));
        Assert.All(method.valueParameters, value =>
        {
            Assert.Equal("byte", value.elementType);
            Assert.Equal("bool", value.managedElementType);
            Assert.Equal(1, value.alignment);
            Assert.Equal(CSharpValueAllocationMode.AddressCheckedPinnedBorrow, value.allocationMode);
        });
        Assert.Empty(plan.valueParameterOmissions);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        string context = sourceRuntime ? "BooleanApiContext" : "BooleanApi";
        await ValueReceiverTests.CompileWithSdkAsync(sink, $$"""
            namespace Generated.BooleanValues;
            public static class BooleanConsumer {
                public static int Use({{context}} context) {
                    bool flag=true;
                    int first=context.Change(in flag,out flag,ref flag);
                    int second=context.ChangeAlias(in flag,out flag,ref flag);
                    return first+second+context.Booleans.Apply(in flag,out flag,ref flag);
                }
            }
            """, sourceRuntime);
        var sources = sink.sources.Select(source => (Path: source.Key, Text: source.Value)).ToArray();
        new EmittedInteropContractReader().Verify(plan, sources, "native");
        foreach (string mutation in new[] { "fixed (bool*", "Unsafe.SkipInit", "address-cast", "in bool", "out bool", "ref bool" })
        {
            string replacement = mutation switch
            {
                "fixed (bool*" => "fixed (byte*", "Unsafe.SkipInit" => "Unsafe.AsRef",
                _ => mutation.Replace("bool", "byte", StringComparison.Ordinal)
            };
            var changed = sources.Select(source =>
            {
                string altered = source.Text.Replace(mutation, replacement, StringComparison.Ordinal);
                if (mutation == "address-cast")
                {
                    SyntaxNode root = CSharpSyntaxTree.ParseText(source.Text).GetRoot();
                    var casts = root.DescendantNodes().OfType<CastExpressionSyntax>().Where(cast => cast.Type is PointerTypeSyntax
                        && cast.Expression is IdentifierNameSyntax address && address.Identifier.ValueText.StartsWith("__bgcsValueAddress", StringComparison.Ordinal)).ToArray();
                    altered = root.ReplaceNodes(casts, (original, _) => original.WithExpression(
                        SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0)).WithTriviaFrom(original.Expression))).ToFullString();
                }
                return (source.Path, Text: altered);
            }).ToArray();
            Assert.Contains(changed, source => source.Text != sink.sources[source.Path]);
            Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan, changed, "native"));
        }
    }

    [Theory]
    [InlineData("Bool32")]
    [InlineData("unknown-size")]
    [InlineData("unknown-alignment")]
    [InlineData("different-alignment")]
    [InlineData("alias-alignment")]
    [InlineData("requested-alignment")]
    [InlineData("integer-convention")]
    [InlineData("const-output")]
    [InlineData("retained")]
    public void BooleanBorrowNeverGuessesWidthConventionAlignmentOrRetention(string fault)
    {
        InteropModule original = Module();
        var types = original.types;
        InteropType boolean = types[0];
        boolean = fault switch
        {
            "Bool32" => boolean with { size = 4, alignment = 4 }, "unknown-size" => boolean with { size = null },
            "unknown-alignment" => boolean with { alignment = null }, "different-alignment" => boolean with { alignment = 2 },
            "integer-convention" => boolean with { primitiveKind = "Bool32", size = 4, alignment = 4 }, _ => boolean
        };
        types = types.SetItem(0, boolean);
        if (fault == "alias-alignment") types = types.SetItem(1, types[1] with { alignment = 2 });
        if (fault == "const-output") types = types.SetItem(1, types[1] with { qualifier = "Const" });
        InteropFunction function = original.functions[0];
        InteropParameter parameter = function.parameters[1];
        parameter = parameter with { singleValue = parameter.singleValue! with
        { requiredAlignment = fault == "requested-alignment" ? 2 : null, retainsPointer = fault == "retained" } };
        function = function with { parameters = function.parameters.SetItem(1, parameter) };
        InteropModule module = new(original.libraryId, original.targetId, original.pointerSize, types, [function], []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        Assert.NotNull(new ValueParameterAnalyzer().GetRestriction(module, function, parameter));
    }

    [Fact]
    public void IntegerBooleanNamesKeepTheirIntegerReferenceCarrier()
    {
        InteropModule original = Module();
        var types = original.types.SetItem(0, original.types[0] with { nativeName = "BOOL", primitiveKind = "Int", size = 4, alignment = 4, isSigned = true });
        types = types.SetItem(1, types[1] with { size = 4, alignment = 4 }).SetItem(2, types[2] with { size = 4, alignment = 4 });
        InteropModule module = new(original.libraryId, original.targetId, original.pointerSize, types, original.functions, []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        ManagedApiModel model = new DefaultManagedProjection().Project(module, new("Generated.IntegerFlags", "Api"), CancellationToken.None);
        CSharpMethodPlan method = Assert.Single(new CSharpTestPlanner().Plan(model, new("native", new NeutralBackendTests.MemorySink())).methods);
        Assert.All(method.parameterTypes, type => Assert.Equal("int*", type));
        Assert.All(method.valueParameters, value => Assert.Equal("int", value.managedElementType));
    }

    private static InteropModule Module()
    {
        Evidence evidence = new(EvidenceLevel.UserAsserted, "fixture:exact-call-only-bool-storage");
        InteropType boolean = new(new("boolean"), "_Bool", NativeTypeCategory.Primitive, 1, 1) { primitiveKind = "Bool", isSigned = false };
        InteropType alias = new(new("alias"), "NativeFlag", NativeTypeCategory.Alias, 1, 1) { elementType = boolean.id };
        InteropType constant = new(new("constant"), "const NativeFlag", NativeTypeCategory.Qualified, 1, 1) { elementType = alias.id, qualifier = "Const" };
        InteropType pointer = new(new("pointer"), "NativeFlag*", NativeTypeCategory.Pointer, 8, 8) { elementType = alias.id };
        InteropType read = new(new("read"), "const NativeFlag*", NativeTypeCategory.Pointer, 8, 8) { elementType = constant.id };
        InteropType integer = new(new("integer"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropParameter Parameter(string id, StableId type, NativeParameterDirection direction) => new(new(id), id, type)
        { singleValue = new(direction, new(NativeLifetimeKind.Call, null, evidence), evidence) { allowsNull = false, retainsPointer = false } };
        InteropFunction function = new(new("change"), "change", integer.id,
            [Parameter("input", read.id, NativeParameterDirection.Input), Parameter("output", pointer.id, NativeParameterDirection.Output), Parameter("state", pointer.id, NativeParameterDirection.InputOutput)], "C");
        return new("boolean-values", "windows-x64-msvc", 8, [boolean, alias, constant, pointer, read, integer], [function], []) { byteOrder = NativeByteOrder.LittleEndian };
    }
}
