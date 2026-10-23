using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core;
using BGCS.Core.Managed;
using BGCS.Core.Requests;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Validation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class EmittedInteropContractTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable)]
    [InlineData(BindingImportMode.DllImport)]
    [InlineData(BindingImportMode.LibraryImport)]
    public async Task PlainFriendlyInspectionPreservesEverySignatureCheckAndParsesTheCompleteActualBody(BindingImportMode mode)
    {
        var model = NeutralBackendTests.Model();
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native_fixture", sink) { importMode = mode };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        string path = sink.sources.Keys.Single(path => path.EndsWith(".Methods.g.cs", StringComparison.Ordinal));
        SyntaxNode root = CSharpSyntaxTree.ParseText(sink.sources[path]).GetRoot();
        MethodDeclarationSyntax method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "Add");
        Assert.NotNull(method.Body);
        Assert.Contains("return this.Raw.Add(left, right);", method.Body!.ToFullString(), StringComparison.Ordinal);
        (string Path, string Text)[] Candidate(SyntaxNode changed) => sink.sources.Select(source => (source.Key,
            source.Key == path ? changed.ToFullString() : source.Value)).ToArray();
        EmittedInteropContractReader reader = new();
        reader.Verify(plan, sink.sources.Select(source => (source.Key, source.Value)), request.libraryName);
        ClassDeclarationSyntax owner = (ClassDeclarationSyntax)method.Parent!;
        MethodDeclarationSyntax overload = (MethodDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration("public int Add(long left, long right) { return 0; }")!;
        reader.Verify(plan, Candidate(root.ReplaceNode(owner, owner.AddMembers(overload))), request.libraryName);
        MethodDeclarationSyntax changedOverload = method.WithReturnType(SyntaxFactory.ParseTypeName("long").WithTriviaFrom(method.ReturnType));
        Assert.Throws<InvalidOperationException>(() => reader.Verify(plan,
            Candidate(root.ReplaceNode(owner, owner.ReplaceNode(method, changedOverload).AddMembers(overload))), request.libraryName));
        Assert.Throws<InvalidOperationException>(() => reader.Verify(plan,
            Candidate(root.ReplaceNode(owner, owner.AddMembers(overload, method))), request.libraryName));
        ParameterSyntax first = method.ParameterList.Parameters[0], last = method.ParameterList.Parameters[^1];
        MethodDeclarationSyntax[] mutations =
        [
            method.WithReturnType(SyntaxFactory.ParseTypeName("long").WithTriviaFrom(method.ReturnType)),
            method.ReplaceNode(first, first.WithType(SyntaxFactory.ParseTypeName("long").WithTriviaFrom(first.Type!))),
            method.ReplaceNode(first, first.WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.RefKeyword).WithTrailingTrivia(SyntaxFactory.Space)))),
            method.ReplaceNode(last, last.WithDefault(SyntaxFactory.EqualsValueClause(SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0))))),
            method.WithModifiers(method.Modifiers.Add(SyntaxFactory.Token(SyntaxKind.StaticKeyword).WithTrailingTrivia(SyntaxFactory.Space))),
            method.AddAttributeLists(SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Attribute(SyntaxFactory.ParseName("global::System.Obsolete"))))),
            method.WithTypeParameterList(SyntaxFactory.TypeParameterList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.TypeParameter("T"))))
        ];
        foreach (MethodDeclarationSyntax changed in mutations)
        {
            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => reader.Verify(plan,
                Candidate(root.ReplaceNode(method, changed)), request.libraryName));
            Assert.Contains("planned public API signature", failure.Message, StringComparison.Ordinal);
        }
        Assert.Throws<InvalidOperationException>(() => reader.Verify(plan, Candidate(root.ReplaceNode(owner, owner.AddMembers(method))), request.libraryName));
        Assert.Throws<InvalidOperationException>(() => reader.Verify(plan, Candidate(root.RemoveNode(method, SyntaxRemoveOptions.KeepNoTrivia)!), request.libraryName));
        MethodDeclarationSyntax malformed = method.WithBody(SyntaxFactory.Block(SyntaxFactory.ParseStatement("return ???;")));
        InvalidOperationException syntax = Assert.Throws<InvalidOperationException>(() => reader.Verify(plan,
            Candidate(root.ReplaceNode(method, malformed)), request.libraryName));
        Assert.Contains("syntactically invalid", syntax.Message, StringComparison.Ordinal);
        reader.Verify(plan, sink.sources.Select(source => (source.Key, source.Value)), request.libraryName);
    }

    [Theory]
    [InlineData(BindingImportMode.FunctionTable)]
    [InlineData(BindingImportMode.DllImport)]
    [InlineData(BindingImportMode.LibraryImport)]
    public async Task MultiTokenPublicSignaturesIgnoreTriviaButRetainEveryTypeTokenAndDeclarationCount(BindingImportMode mode)
    {
        const string managedType = "global::System.ValueTuple<int, global::System.ValueTuple<int, int>>";
        ManagedApiModel original = NeutralBackendTests.Model();
        ManagedRecipeProjection projection = new(managedType, "{value}.Item1");
        ManagedApiModel model = original with
        {
            methods = original.methods.Select(method => method.name == "Add" ? method with
            { valueProjections = [new(new("left"), projection), new(new("right"), projection)] } : method).ToImmutableArray()
        };
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native_fixture", sink) { importMode = mode };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        string path = sink.sources.Keys.Single(path => path.EndsWith(".Methods.g.cs", StringComparison.Ordinal));
        SyntaxNode root = CSharpSyntaxTree.ParseText(sink.sources[path]).GetRoot();
        MethodDeclarationSyntax method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "Add");
        TypeSyntax parameter = method.ParameterList.Parameters[0].Type!;
        string Edited(TypeSyntax replacement) => root.ReplaceNode(parameter, replacement.WithTrailingTrivia(SyntaxFactory.Space)).ToFullString();
        (string Path, string Text)[] Candidate(string text) => sink.sources.Select(source => (source.Key, source.Key == path ? text : source.Value)).ToArray();
        EmittedInteropContractReader reader = new();
        string trivia = Edited(SyntaxFactory.ParseTypeName("global /* root */ :: System . ValueTuple < int , global :: System . ValueTuple < int , /* item */ int > >"));
        reader.Verify(plan, Candidate(trivia), request.libraryName);
        foreach (string changedType in new[]
        {
            "global::System.ValueTuple<int, global::System.ValueTuple<int, long>>",
            "global::System.ValueTuple<global::System.ValueTuple<int, int>, int>",
            managedType + "[]"
        })
        {
            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => reader.Verify(plan,
                Candidate(Edited(SyntaxFactory.ParseTypeName(changedType))), request.libraryName));
            Assert.Contains("planned public API signature", failure.Message, StringComparison.Ordinal);
        }
        ClassDeclarationSyntax owner = (ClassDeclarationSyntax)method.Parent!;
        string duplicate = root.ReplaceNode(owner, owner.AddMembers(method)).ToFullString();
        Assert.Throws<InvalidOperationException>(() => reader.Verify(plan, Candidate(duplicate), request.libraryName));
        reader.Verify(plan, sink.sources.Select(source => (source.Key, source.Value)), request.libraryName);
    }

    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task CompleteSources_AreRecheckedForAllImportModesAndLayouts(
        BindingImportMode mode,
        bool singleFile
    ) {
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native_fixture", sink) { importMode = mode, singleFile = singleFile };
        var model = NeutralBackendTests.ModelWithStorage();
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        new EmittedInteropContractReader().Verify(plan, sink.sources.Select(pair => (pair.Key, pair.Value)), request.libraryName);
    }

    [Theory]
    [InlineData("offset")]
    [InlineData("size")]
    [InlineData("pack")]
    [InlineData("enum")]
    [InlineData("array")]
    public async Task EditedStorage_RejectsChangedOffsetsExtentsAndIntegralCarriers(string change)
    {
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native_fixture", sink);
        var model = NeutralBackendTests.ModelWithStorage();
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        var edited = sink.sources.Select(pair => (pair.Key, change switch
        {
            "offset" => pair.Value.Replace("FieldOffset(32)", "FieldOffset(31)", StringComparison.Ordinal),
            "size" => pair.Value.Replace("Size = 96", "Size = 97", StringComparison.Ordinal),
            "pack" => pair.Value.Replace("Pack = 1", "Pack = 8", StringComparison.Ordinal),
            "enum" => pair.Value.Replace("enum Flags : ulong", "enum Flags : uint", StringComparison.Ordinal),
            "array" => pair.Value.Replace("InlineArray(3)", "InlineArray(4)", StringComparison.Ordinal),
            _ => throw new ArgumentException(nameof(change))
        }));
        Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan, edited, request.libraryName));
    }

    [Theory]
    [InlineData(BindingImportMode.FunctionTable, "carrier")]
    [InlineData(BindingImportMode.FunctionTable, "convention")]
    [InlineData(BindingImportMode.FunctionTable, "export")]
    [InlineData(BindingImportMode.FunctionTable, "shared-field")]
    [InlineData(BindingImportMode.DllImport, "carrier")]
    [InlineData(BindingImportMode.DllImport, "convention")]
    [InlineData(BindingImportMode.DllImport, "export")]
    [InlineData(BindingImportMode.DllImport, "library")]
    [InlineData(BindingImportMode.LibraryImport, "carrier")]
    [InlineData(BindingImportMode.LibraryImport, "convention")]
    [InlineData(BindingImportMode.LibraryImport, "export")]
    [InlineData(BindingImportMode.LibraryImport, "library")]
    public async Task EditedActualImports_AreRejectedEvenWhenTheOriginalPlanIsUnchanged(
        BindingImportMode mode,
        string change
    ) {
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native_fixture", sink) { importMode = mode };
        var model = NeutralBackendTests.Model();
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        (string Path, string Text)[] edited = sink.sources.Select(pair => (pair.Key, change switch
        {
            "carrier" => pair.Value.Replace("<int, int, int>", "<long, int, int>", StringComparison.Ordinal).Replace("int Function0(int", "int Function0(long", StringComparison.Ordinal),
            "convention" => pair.Value.Replace("Cdecl", "Stdcall", StringComparison.Ordinal),
            "export" => pair.Value.Replace("\"add\"", "\"different_export\"", StringComparison.Ordinal),
            "library" => pair.Value.Replace("\"native_fixture\"", "\"different_library\"", StringComparison.Ordinal),
            "shared-field" => pair.Value.Replace("private readonly delegate*", "private static delegate*", StringComparison.Ordinal),
            _ => throw new ArgumentException(nameof(change))
        })).ToArray();
        Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan, edited, request.libraryName));
    }

    [Theory]
    [InlineData("[DllImport(\"hidden\")] delegate int Hidden();")]
    [InlineData("class Extra { [LibraryImport(\"hidden\")] public int Value; }")]
    [InlineData("class Extra { public int Value { [DllImport(\"hidden\")] get => 0; } }")]
    [InlineData("class Extra { public void Method() { [DllImport(\"hidden\")] static extern int Hidden(); } }")]
    [InlineData("class Extra { public void Method() { System.Func<int> hidden = [DllImport(\"hidden\")] () => 0; } }")]
    [InlineData("struct Extra { public class Nested { [DllImport(\"hidden\")] public static extern int Hidden(); } }")]
    [InlineData("record Extra { [LibraryImport(\"hidden\")] public static partial int Hidden(); }")]
    [InlineData("[assembly: Foreign.DllImportAttribute(\"hidden\")] class Extra {}")]
    public async Task IndexedFunctionTableValidationRetainsTheGlobalImportAttributeGuard(string addition)
    {
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native_fixture", sink);
        var model = NeutralBackendTests.Model();
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        sink.sources.Add("Additional.cs", addition);
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan,
            sink.sources.Select(source => (source.Key, source.Value)), request.libraryName));
        Assert.Contains("undeclared runtime imports", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("class Extra { const string Text = \"[DllImport(\\\"hidden\\\")]\"; }")]
    [InlineData("#if BGCS_INACTIVE_IMPORT\n[DllImport(\"hidden\")] class Hidden {}\n#endif\nclass Extra {}")]
    public async Task IndexedFunctionTableValidationIgnoresOnlySyntaxInertImportText(string addition)
    {
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native_fixture", sink);
        var model = NeutralBackendTests.Model();
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        sink.sources.Add("Additional.cs", addition);
        new EmittedInteropContractReader().Verify(plan, sink.sources.Select(source => (source.Key, source.Value)), request.libraryName);
    }

    [Fact]
    public async Task ConcurrentValidationKeepsActualCandidateIndexesIndependentForTheSamePathsAndPlan()
    {
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native_fixture", sink);
        var model = NeutralBackendTests.Model();
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        (string Path, string Text)[] original = sink.sources.Select(source => (source.Key, source.Value)).ToArray();
        (string Path, string Text)[] changed = original.Select(source => (source.Path,
            source.Text + "\n[Foreign.NativeNameAttribute] class UnexpectedMetadata {}\n")).ToArray();
        EmittedInteropContractReader reader = new();
        await Task.WhenAll(Enumerable.Range(0, 12).Select(index => Task.Run(() =>
        {
            if (index % 2 == 0) reader.Verify(plan, original, request.libraryName);
            else
            {
                InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => reader.Verify(plan, changed, request.libraryName));
                Assert.Contains("while metadata was disabled", failure.Message, StringComparison.Ordinal);
            }
        })));
        reader.Verify(plan, original, request.libraryName);
    }
}
