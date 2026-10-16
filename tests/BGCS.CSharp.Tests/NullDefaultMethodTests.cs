using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Analysis.Safety;
using BGCS.Contracts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Managed;
using BGCS.Contracts.Native;
using BGCS.Contracts.Requests;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using BGCS.CSharp.Validation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class NullDefaultMethodTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task NullOnlyCallsPreserveCheckedInputsAndDoNotExposeAnUnprovedPointerOverload(BindingImportMode mode, bool sourceRuntime)
    {
        ManagedApiModel model = Model(null, sourceRuntime);
        InteropFunction function = Assert.Single(model.interop.functions);
        InteropParameter optional = function.parameters[1];
        Assert.NotNull(new ValueParameterAnalyzer().GetRestriction(model.interop, function, optional));
        Assert.Null(new ValueParameterAnalyzer().GetNullBindingRestriction(model.interop, function, optional));
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native", sink) { importMode = mode, includeRuntimeSource = sourceRuntime,
            runtimeNamespace = sourceRuntime ? "Generated.NullDefaults.Runtime" : "BGCS.Runtime", singleFile = sourceRuntime };
        CSharpEmissionPlan plan = new CSharpEmissionPlanner().Plan(model, request);
        CSharpNullDefaultMethodPlan operation = Assert.Single(plan.nullDefaultMethods);
        Assert.Equal(new StableId("optional"), Assert.Single(operation.nullInputs).nativeParameter);
        Assert.Equal(new[] { 0, 2 }, operation.signature.inputIndices);
        Assert.Equal(new[] { "string", "uint" }, operation.signature.parameterTypes);
        Assert.All(plan.defaultOverloads, overload => Assert.DoesNotContain("byte*", overload.signature.parameterTypes));
        Assert.Equal(new[] { "byte*", "byte*", "uint" }, Assert.Single(plan.methods).parameterTypes);
        await new CSharpBackend().EmitAsync(model, request, CancellationToken.None);
        int augmented = 0;
        foreach (string path in sink.sources.Keys.ToArray())
        {
            SyntaxNode root = CSharpSyntaxTree.ParseText(sink.sources[path]).GetRoot();
            ClassDeclarationSyntax? context = root.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault(type => type.Identifier.ValueText == plan.contextName
                && type.Members.OfType<MethodDeclarationSyntax>().Any(method => method.Identifier.ValueText == "Begin" && method.ParameterList.Parameters.Count == 2));
            if (context is null) continue;
            augmented++;
            root = root.ReplaceNode(context, context.AddMembers(SyntaxFactory.ParseMemberDeclaration("public int Begin(int value)=>value;")!,
                SyntaxFactory.ParseMemberDeclaration("private void Begin(byte* first,byte* second) {}")!));
            sink.sources[path] = root.ToFullString();
        }
        Assert.Equal(1, augmented);
        // A custom partial overload does not replace the checked operation or its hidden native pointer channel.
        new EmittedInteropContractReader().Verify(plan, sink.sources.Select(source => (source.Key, source.Value)).ToArray(), "native");
        await ValueReceiverTests.CompileWithSdkAsync(sink, $$"""
            namespace Generated.NullDefaults;
            public static class NullDefaultConsumer {
                public static int Use({{plan.contextName}} context) => context.Begin("AB",0)+context.Begin("AB")
                    +context.BeginAlias("AB")+context.Begins.Open("AB")+context.Begin(5);
            }
            """, sourceRuntime);
        var sources = sink.sources.Select(source => (Path: source.Key, Text: source.Value)).ToArray();
        new EmittedInteropContractReader().Verify(plan, sources, "native");
        foreach (string mutation in new[] { "null", "lease", "codec", "forward", "unplanned-overload" })
        {
            int edits = 0;
            var changed = sources.Select(source =>
            {
                SyntaxNode root = CSharpSyntaxTree.ParseText(source.Text).GetRoot();
                if (mutation == "null")
                {
                    var casts = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Where(method => method.Identifier.ValueText == "Begin" && method.ParameterList.Parameters.Count == 2)
                        .SelectMany(method => method.DescendantNodes().OfType<CastExpressionSyntax>()).Where(cast => cast.Type is PointerTypeSyntax
                            && cast.Expression is LiteralExpressionSyntax literal && literal.Token.ValueText == "0").ToArray();
                    edits += casts.Length;
                    root = root.ReplaceNodes(casts, (original, _) => original.WithExpression(SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(1))));
                }
                else if (mutation == "unplanned-overload")
                {
                    ClassDeclarationSyntax? context = root.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault(type => type.Identifier.ValueText == plan.contextName
                        && type.Members.OfType<MethodDeclarationSyntax>().Any(method => method.Identifier.ValueText == "Begin" && method.ParameterList.Parameters.Count == 2));
                    if (context is not null)
                    {
                        edits++;
                        root = root.ReplaceNode(context, context.AddMembers(SyntaxFactory.ParseMemberDeclaration("public int Begin(byte* label,byte* optional,uint flags)=>Raw.Begin(label,optional,flags);")!));
                    }
                }
                string altered = root.ToFullString();
                if (mutation is "lease" or "codec" or "forward")
                {
                    string token = mutation switch { "lease" => ".AcquireCall()", "codec" => ".EncodeNullTerminated(", _ => "this.Begin(" };
                    string replace = mutation switch { "lease" => ".AcquireCleanupCall()", "codec" => ".EncodeCounted(", _ => "this.Raw.Begin(" };
                    if (altered.Contains(token, StringComparison.Ordinal)) edits++;
                    altered = altered.Replace(token, replace, StringComparison.Ordinal);
                }
                return (source.Path, Text: altered);
            }).ToArray();
            Assert.True(edits > 0, mutation);
            Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan, changed, "native"));
        }
    }

    [Theory]
    [InlineData("missing-nullability")]
    [InlineData("unknown-nullability")]
    [InlineData("missing-contract")]
    [InlineData("retained")]
    [InlineData("unknown-retention")]
    [InlineData("owner-lifetime")]
    [InlineData("inferred")]
    [InlineData("text-conflict")]
    [InlineData("second-pointer")]
    [InlineData("no-null-default")]
    [InlineData("nonnull-default")]
    [InlineData("callback")]
    public async Task IncompleteOrConflictingOptionalPointersRemainRawInsteadOfReceivingAGeneralNullableReference(string fault)
    {
        ManagedApiModel model = Model(fault, false);
        NeutralBackendTests.MemorySink sink = new();
        await Assert.ThrowsAnyAsync<Exception>(async () => await new CSharpBackend().EmitAsync(model, new("native", sink), CancellationToken.None));
        Assert.Empty(sink.sources);
        Assert.Single(model.interop.functions);
        Assert.Equal(NativeTypeCategory.Pointer, model.interop.GetType(model.interop.functions[0].parameters[1].type).category);
    }

    private static ManagedApiModel Model(string? fault, bool sourceRuntime)
    {
        Evidence evidence = new(EvidenceLevel.UserAsserted, "fixture:explicit-optional-value");
        InteropType octet = new(new("byte"), "unsigned char", NativeTypeCategory.Primitive, 1, 1) { primitiveKind = "UnsignedChar", isSigned = false };
        InteropType pointer = new(new("pointer"), "unsigned char*", NativeTypeCategory.Pointer, 8, 8) { elementType = octet.id };
        InteropType integer = new(new("integer"), "unsigned int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "UnsignedInt", isSigned = false };
        InteropType result = new(new("result"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropParameter label = new(new("label"), "label", pointer.id) { text = new(NativeTextEncoding.Utf8, 1, NativeLengthUnit.CodeUnits,
            NativeParameterDirection.Input, new(NativeLifetimeKind.Call, null, evidence), evidence) { zeroTerminated = true, allowsNull = false, retainsPointer = false } };
        ValueParameterContract value = new(NativeParameterDirection.InputOutput, new(NativeLifetimeKind.Call, null, evidence), evidence) { allowsNull = true, retainsPointer = false };
        value = fault switch
        {
            "missing-nullability" => value with { allowsNull = false }, "unknown-nullability" => value with { allowsNull = null },
            "retained" => value with { retainsPointer = true }, "unknown-retention" => value with { retainsPointer = null },
            "owner-lifetime" => value with { lifetime = value.lifetime with { kind = NativeLifetimeKind.Owner } },
            "inferred" => value with { evidence = new(EvidenceLevel.Inferred, "guess") }, _ => value
        };
        InteropParameter optional = new(new("optional"), "optional", pointer.id) { singleValue = fault == "missing-contract" ? null : value, text = fault == "text-conflict" ? label.text : null };
        ImmutableArray<InteropParameter> parameters = [label, optional, new(new("flags"), "flags", integer.id)];
        if (fault == "second-pointer") parameters = parameters.Add(new(new("second"), "second", pointer.id));
        ImmutableArray<InteropType> types = [octet, pointer, integer, result];
        if (fault == "callback")
        {
            InteropType callable = new(new("callable"), "int(void)", NativeTypeCategory.Function, null, null) { returnType = result.id, callingConvention = "C" };
            InteropType address = new(new("callback-pointer"), "int(*)(void)", NativeTypeCategory.Pointer, 8, 8) { elementType = callable.id };
            types = types.Add(callable).Add(address);
            parameters = parameters.SetItem(1, optional with { type = address.id });
        }
        InteropFunction function = new(new("begin"), "begin", result.id, parameters, "C");
        ManagedDefaultValue nullable = fault == "nonnull-default" ? new(ManagedDefaultValueKind.Integer, "1") : new(ManagedDefaultValueKind.Null);
        ImmutableArray<ManagedDefaultArgumentRequest> defaults = fault == "no-null-default"
            ? [new("flags", new(ManagedDefaultValueKind.Integer, "0"))] : [new("optional", nullable), new("flags", new(ManagedDefaultValueKind.Integer, "0"))];
        return new DefaultManagedProjection().Project(new("null-defaults", "windows-x64-msvc", 8, types, [function], []),
            new("Generated.NullDefaults", "Api") { style = sourceRuntime ? ManagedApiStyle.Static : ManagedApiStyle.Instance,
                functions = [new("begin") { defaults = defaults }], functionAliases = [new("begin", "BeginAlias")],
                functionContainers = [new("Begins", [new("begin", "Open")])] }, CancellationToken.None);
    }
}
