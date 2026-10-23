using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Managed;
using BGCS.Core.Requests;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Validation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class NativeMetadataDisabledGuardTests
{
    [Theory]
    [InlineData("class Extra { [NativeName] public void Method() {} }")]
    [InlineData("class Extra { public void Method([NativeNameAttribute] int value) {} }")]
    [InlineData("class Extra { [return: Foreign.NativeName] public int Method() => 0; }")]
    [InlineData("[global::Foreign.NativeNameAttribute] class Extra {}")]
    [InlineData("class Extra { [Foreign.NativeNameAttribute] public int Value; }")]
    [InlineData("class Extra { [NativeName] public int Value { get; } }")]
    [InlineData("class Extra { public int Value { [NativeName] get => 0; } }")]
    [InlineData("[assembly: global::Foreign.NativeName] class Extra {}")]
    [InlineData("class Extra { public void Method() { [NativeNameAttribute] void Local() {} } }")]
    [InlineData("class Extra { public void Method() { System.Func<int, int> callback = [NativeName] (int value) => value; } }")]
    public async Task DisabledMetadataRejectsUnauthorizedAttributesAtEveryActualSyntaxLocation(string addition)
    {
        var fixture = await Generate();
        fixture.Sink.sources.Add("Additional.cs", addition);
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => Verify(fixture));
        Assert.Contains("while metadata was disabled", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[NativeNames] class Extra {}")]
    [InlineData("[Foreign.NativeNameExtraAttribute] class Extra {}")]
    [InlineData("/// <summary>NativeNameAttribute is ordinary documentation.</summary>\nclass Extra { const string Text = \"[NativeName]\"; }")]
    [InlineData("#if BGCS_INACTIVE_METADATA\n[NativeNameAttribute] class Inactive {}\n#endif\nclass Extra {}")]
    [InlineData("class NativeNameAttribute : System.Attribute {}")]
    public async Task DisabledMetadataAllowsUnrelatedAttributesDefinitionsAndSyntaxInertText(string addition)
    {
        var fixture = await Generate();
        fixture.Sink.sources.Add("Additional.cs", addition);
        Verify(fixture);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisabledMetadataStillRejectsChangedAndDuplicatedNativeTableCarriers(bool duplicate)
    {
        var fixture = await Generate();
        string key = Assert.Single(fixture.Sink.sources.Keys, path => path.EndsWith("Api/" + fixture.Plan.contextName + ".g.cs", StringComparison.Ordinal));
        CompilationUnitSyntax root = CSharpSyntaxTree.ParseText(fixture.Sink.sources[key]).GetCompilationUnitRoot();
        ClassDeclarationSyntax context = Assert.Single(root.DescendantNodes().OfType<ClassDeclarationSyntax>(), type => type.Identifier.ValueText == fixture.Plan.contextName);
        FieldDeclarationSyntax field = Assert.Single(context.Members.OfType<FieldDeclarationSyntax>(), member => member.Declaration.Variables.Any(variable => variable.Identifier.ValueText == "m_function0"));
        ClassDeclarationSyntax changed = duplicate ? context.AddMembers(field)
            : context.ReplaceNode(field, field.WithDeclaration(field.Declaration.WithType(SyntaxFactory.ParseTypeName("delegate* unmanaged[Cdecl]<long, long, long>"))));
        fixture.Sink.sources[key] = root.ReplaceNode(context, changed).ToFullString();
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => Verify(fixture));
        Assert.Contains("function table", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<(CSharpEmissionPlan Plan, NeutralBackendTests.MemorySink Sink, EmissionRequest Request)> Generate()
    {
        ManagedApiModel model = NeutralBackendTests.Model();
        Assert.False(model.emitNativeMetadata);
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("metadata_disabled", sink);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        return (new CSharpTestPlanner().Plan(model, request), sink, request);
    }

    private static void Verify((CSharpEmissionPlan Plan, NeutralBackendTests.MemorySink Sink, EmissionRequest Request) fixture)
        => new EmittedInteropContractReader().Verify(fixture.Plan, fixture.Sink.sources.Select(source => (source.Key, source.Value)), fixture.Request.libraryName);
}
