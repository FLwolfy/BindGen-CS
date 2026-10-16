using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Managed;
using BGCS.Contracts.Requests;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Validation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class EmittedInteropIndexTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegalDeclarationsAndHarmlessBodyAttributesRemainAccepted(bool metadata)
    {
        Candidate candidate = await CreateAsync(metadata);
        const string extra = """
            #nullable enable
            class Extra {
                public class Nested {}
                public struct S {}
                public enum E { A }
                public delegate int D();
                public int P => 42;
                public int[] A = new[] { 1, 2 };
                public System.Func<int> F = [System.Obsolete] () => 42;
                public System.Func<int> G = delegate { return 42; };
                public void M() {
                    [System.Obsolete] static int Local(int x) => x;
                    System.Func<int> f = () => Local(42);
                }
            }
            record R { public class Nested {} }
            interface I {}
            class HarmlessDllImportAttribute<T> : System.Attribute {}
            class GenericAttributeUse { [HarmlessDllImport<int>] public int F() => 42; }
            """;
        Verify(candidate, extra[..extra.IndexOf("class HarmlessDllImportAttribute", StringComparison.Ordinal)]);
        Verify(candidate, extra);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnicodeCommentsStringsAndInactiveAttributesAreNotImports(bool metadata)
    {
        Candidate candidate = await CreateAsync(metadata);
        const string extra = """
            #if NEVER
            [NativeName] class Hidden {}
            #endif
            class Δ {
                public int λ => 42;
                string Text = "NativeName DllImport LibraryImport";
                // NativeName DllImport LibraryImport \u004e
                public void M() { /* NativeName DllImport */ }
            }
            """;
        Verify(candidate, extra);
    }

    public static IEnumerable<object[]> ReservedBodyAttributes()
    {
        string[] sources =
        [
            "class Extra { void M() { [NativeName] static void Hidden() {} } }",
            "class Extra { void M() { System.Func<int> f = [NativeNameAttribute] () => 0; } }",
            "class Extra { System.Func<int> F = [NativeName] () => 0; }",
            "class Extra { System.Func<int> F() => [NativeName] () => 0; }",
            "class Extra { void M() { [return: NativeName] static int Hidden() => 0; } }",
            "class Extra { void M() { [@NativeNameAttribute] static void Hidden() {} } }",
            """class Extra { void M() { [\u004eativeName] static void Hidden() {} } }""",
            """class Extra { void M() { [\U0000004eativeName] static void Hidden() {} } }""",
            "class Extra { void M() { [NativeNa\u200dme] static void Hidden() {} } }"
        ];
        foreach (bool metadata in new[] { false, true })
            foreach (string source in sources)
                yield return [metadata, source];
    }

    [Theory]
    [MemberData(nameof(ReservedBodyAttributes))]
    public async Task ReservedAttributesInsideEveryBodyFormRemainRejected(bool metadata, string source)
    {
        Candidate candidate = await CreateAsync(metadata);
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => Verify(candidate, source));
        Assert.Contains("Native name attributes", failure.Message);
    }

    public static IEnumerable<object[]> BodyImports()
    {
        string[] sources =
        [
            """class Extra { void M() { [DllImport("hidden")] static extern int Hidden(); } }""",
            """class Extra { void M() { System.Func<int> f = [LibraryImport("hidden")] () => 0; } }""",
            """class Extra { void M() { [PrefixDllImportAttribute("hidden")] static extern int Hidden(); } }""",
            """class Extra { void M() { [Foreign.PrefixLibraryImport("hidden")] static extern int Hidden(); } }""",
            """class Extra { System.Func<int> F = [DllImport("hidden")] () => 0; }""",
            """class Extra { System.Func<int> F() => [LibraryImport("hidden")] () => 0; }""",
            """class Extra { void M() { [Dll\u0049mport("hidden")] static extern int Hidden(); } }""",
            "class Extra { System.Func<int> F = [Dll\u200dImport(\"hidden\")] () => 0; }",
            """class Extra { System.Func<int> F() => [LibraryIm\u0070ortAttribute("hidden")] () => 0; }"""
        ];
        foreach (bool metadata in new[] { false, true })
            foreach (string source in sources)
                yield return [metadata, source];
    }

    [Theory]
    [MemberData(nameof(BodyImports))]
    public async Task BodyAndInitializerImportsRetainConservativeSuffixChecks(bool metadata, string source)
    {
        Candidate candidate = await CreateAsync(metadata);
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => Verify(candidate, source));
        Assert.Contains("undeclared runtime imports", failure.Message);
    }

    public static IEnumerable<object[]> EscapedImports()
    {
        (string Spelling, bool LibraryImport)[] spellings =
        [
            (@"Dll\u0049mport", false),
            ("Dll\u200dImport", false),
            (@"DllIm\u0070ortAttribute", false),
            (@"\u0044llImport", false),
            (@"LibraryIm\u0070ortAttribute", true)
        ];
        foreach (bool metadata in new[] { false, true })
            foreach ((string spelling, bool libraryImport) in spellings)
                yield return [metadata, spelling, libraryImport];
    }

    [Theory]
    [MemberData(nameof(EscapedImports))]
    public async Task CompilerRecognizedEscapedImportsCannotEscapeTheGlobalGuard(bool metadata, string spelling, bool libraryImport)
    {
        Candidate candidate = await CreateAsync(metadata);
        string source = "partial class Extra { [global::System.Runtime.InteropServices." + spelling
            + "(\"hidden\")] public static " + (libraryImport ? "partial" : "extern") + " int Hidden(); }";
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, new(LanguageVersion.CSharp14), "Extra.cs");
        Assert.DoesNotContain(tree.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        CSharpCompilation compilation = CSharpCompilation.Create("EscapedImport", [tree], CompilerReferences.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        MethodDeclarationSyntax method = Assert.Single(tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>());
        IMethodSymbol symbol = Assert.IsAssignableFrom<IMethodSymbol>(compilation.GetSemanticModel(tree).GetDeclaredSymbol(method));
        if (libraryImport)
        {
            // The attribute is recognized; generating its partial implementation is a separate compiler stage.
            Assert.Contains(symbol.GetAttributes(), attribute => attribute.AttributeClass?.ToDisplayString()
                == "System.Runtime.InteropServices.LibraryImportAttribute");
        }
        else
        {
            Assert.Equal("hidden", symbol.GetDllImportData()?.ModuleName);
            Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        }
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => Verify(candidate, source));
        Assert.Contains("undeclared runtime imports", failure.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IllegalBodyDeclarationsStillFailFullSyntaxValidation(bool metadata)
    {
        Candidate candidate = await CreateAsync(metadata);
        foreach (string extra in new[]
        {
            "class Extra { void M() { class Local {} } }",
            "class Extra { void M() { delegate int D(); } }",
            "class Extra { int P = ; }"
        })
        {
            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => Verify(candidate, extra));
            Assert.Contains("syntactically invalid", failure.Message);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LazyInputsAreEnumeratedOnceAndSyntaxErrorsKeepTheirOriginalPriority(bool metadata)
    {
        Candidate candidate = await CreateAsync(metadata);
        int enumerations = 0;
        IEnumerable<(string Path, string Text)> Valid()
        {
            Assert.Equal(1, ++enumerations);
            foreach (var source in candidate.sources) yield return source;
            yield return ("Extra.cs", "class Extra { int F() => 42; }");
        }
        new EmittedInteropContractReader().Verify(candidate.plan, Valid(), "native_fixture");
        Assert.Equal(1, enumerations);

        bool reachedLaterInput = false;
        IEnumerable<(string Path, string Text)> Invalid()
        {
            foreach (var source in candidate.sources) yield return source;
            yield return ("First.cs", "class First { int P = ; }");
            reachedLaterInput = true;
            throw new InvalidOperationException("The later input must not be reached.");
        }
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            new EmittedInteropContractReader().Verify(candidate.plan, Invalid(), "native_fixture"));
        Assert.Contains("syntactically invalid", failure.Message);
        Assert.Contains("First.cs", failure.Message);
        Assert.False(reachedLaterInput);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationIsPreservedBeforeParsingAndAfterLazyInputEnumeration(bool metadata)
    {
        Candidate candidate = await CreateAsync(metadata);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            new EmittedInteropContractReader().Verify(candidate.plan, candidate.sources, "native_fixture", cancellation.Token));

        using CancellationTokenSource duringEnumeration = new();
        IEnumerable<(string Path, string Text)> Inputs()
        {
            foreach (var source in candidate.sources) yield return source;
            duringEnumeration.Cancel();
        }
        Assert.Throws<OperationCanceledException>(() =>
            new EmittedInteropContractReader().Verify(candidate.plan, Inputs(), "native_fixture", duringEnumeration.Token));
    }

    private static readonly Lazy<MetadataReference[]> CompilerReferences = new(() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToArray());

    private static async Task<Candidate> CreateAsync(bool metadata)
    {
        ManagedApiModel model = NeutralBackendTests.Model() with { emitNativeMetadata = metadata };
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native_fixture", sink);
        CSharpEmissionPlan plan = new CSharpEmissionPlanner().Plan(model, request);
        await new CSharpBackend().EmitAsync(model, request, CancellationToken.None);
        return new(plan, sink.sources.Select(source => (source.Key, source.Value)).ToArray());
    }

    private static void Verify(Candidate candidate, string extra)
        => new EmittedInteropContractReader().Verify(candidate.plan,
            candidate.sources.Append(("Extra.cs", extra)), "native_fixture");

    private sealed record Candidate(CSharpEmissionPlan plan, (string Path, string Text)[] sources);
}
