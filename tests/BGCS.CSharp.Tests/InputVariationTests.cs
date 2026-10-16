using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Managed;
using BGCS.Contracts.Requests;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using BGCS.CSharp.Validation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class InputVariationTests
{
    [Theory]
    [InlineData(ManagedApiStyle.Instance, false)]
    [InlineData(ManagedApiStyle.Instance, true)]
    [InlineData(ManagedApiStyle.Static, false)]
    [InlineData(ManagedApiStyle.Static, true)]
    public async Task AlternateInputsShareTheOriginalImportAndCheckedForwarding(ManagedApiStyle style, bool singleFile)
    {
        ManagedApiModel original = NeutralBackendTests.Model();
        ManagedApiModel model = new DefaultManagedProjection().Project(original.interop, new("Generated.Example", "NativeApi")
        { style = style, functions = [new("add") { inputVariations = [new([new("left", new("long", "checked((int){value})"))]),
            new([new("left", new("short", "(int){value}"))]) { name = "AddShort" }] }] }, CancellationToken.None);
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native", sink) { singleFile = singleFile, includeRuntimeSource = singleFile, runtimeNamespace = singleFile ? "Generated.Example.Runtime" : "BGCS.Runtime" };
        CSharpEmissionPlan plan = new CSharpEmissionPlanner().Plan(model, request);
        Assert.Equal(original.methods.Length, plan.methods.Length);
        Assert.Equal(2, plan.inputVariations.Length);
        Assert.Same(original.interop, model.interop);
        await new CSharpBackend().EmitAsync(model, request, CancellationToken.None);
        string[] platform = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        CSharpCompilation compilation = CSharpCompilation.Create("InputVariations_" + Guid.NewGuid().ToString("N"),
            sink.sources.Select(source => CSharpSyntaxTree.ParseText(source.Value, new(LanguageVersion.CSharp14), source.Key)),
            platform.Append(typeof(BGCS.Runtime.NativeModuleSession).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path)),
            new(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error));
        using MemoryStream image = new();
        var emitted = compilation.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        INamedTypeSymbol context = compilation.GetTypeByMetadataName("Generated.Example." + plan.contextName)!;
        Assert.Equal(2, context.GetMembers("Add").OfType<IMethodSymbol>().Count());
        Assert.Single(context.GetMembers("AddShort"));
        if (style == ManagedApiStyle.Static) Assert.Equal(2, compilation.GetTypeByMetadataName("Generated.Example.NativeApi")!.GetMembers("Add").OfType<IMethodSymbol>().Count());
        var changed = sink.sources.Select(source =>
        {
            CompilationUnitSyntax root = SyntaxFactory.ParseCompilationUnit(source.Value);
            MethodDeclarationSyntax? variation = root.DescendantNodes().OfType<MethodDeclarationSyntax>().SingleOrDefault(method => method.Identifier.ValueText == "AddShort" && !method.Modifiers.Any(token => token.IsKind(SyntaxKind.StaticKeyword)));
            return (source.Key, variation is null ? source.Value : root.ReplaceNode(variation.ExpressionBody!.Expression,
                SyntaxFactory.ParseExpression("this.Add(0, right)")).NormalizeWhitespace().ToFullString());
        });
        Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan, changed, "native"));
    }

    [Theory]
    [InlineData("same-signature")]
    [InlineData("duplicate-parameter")]
    [InlineData("absent-parameter")]
    [InlineData("no-input-conversion")]
    [InlineData("limit")]
    public async Task InvalidOrUnboundedVariationsRejectBeforeAnyCandidateWrite(string fault)
    {
        ManagedApiModel model = NeutralBackendTests.Model();
        ManagedMember method = model.methods[0];
        ManagedRecipeProjection projection = new(fault == "same-signature" ? "int" : "long", fault == "no-input-conversion" ? null : "checked((int){value})", fault == "no-input-conversion" ? "{value}" : null);
        var parameter = new ManagedValueProjection(fault == "absent-parameter" ? new("absent") : model.interop.functions[0].parameters[0].id, projection);
        ManagedInputVariation variation = new(fault == "duplicate-parameter" ? [parameter, parameter] : [parameter]);
        method = method with { inputVariations = fault == "limit" ? [variation, variation with { name = "Alternative" }] : [variation] };
        model = model with { methods = model.methods.SetItem(0, method), defaultOverloadLimit = fault == "limit" ? 1 : 128 };
        NeutralBackendTests.MemorySink sink = new();
        Exception? error = await Record.ExceptionAsync(async () => await new CSharpBackend().EmitAsync(model, new("native", sink), CancellationToken.None));
        Assert.True(error is ArgumentException or InvalidOperationException, error?.ToString());
        Assert.Empty(sink.sources);
    }
}
