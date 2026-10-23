using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BGCS.CSharp.Emission;

internal static class RuntimeFrameworkQualifier
{
    internal static ImmutableArray<(string Path, string Text)> Qualify(
        ImmutableArray<(string Path, string Text)> sources,
        CancellationToken cancellationToken
    ) {
        string frameworkDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        string[] assemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? throw new InvalidOperationException("Framework metadata for runtime source validation is unavailable."))
            .Split(Path.PathSeparator).Where(path => string.Equals(Path.GetDirectoryName(path), frameworkDirectory, StringComparison.Ordinal)).ToArray();
        SyntaxTree[] trees = sources.Select(source => CSharpSyntaxTree.ParseText(source.Text, new CSharpParseOptions(LanguageVersion.CSharp14), source.Path, cancellationToken: cancellationToken)).ToArray();
        CSharpCompilation compilation = CSharpCompilation.Create("BGCS.RuntimeSource.Validation", trees,
            assemblies.Select(path => MetadataReference.CreateFromFile(path)), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        Diagnostic[] errors = compilation.GetDiagnostics(cancellationToken).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length != 0) throw new InvalidOperationException("The selected runtime dependency closure is incomplete: " + string.Join("; ", errors.Select(error => error.ToString())));
        ImmutableArray<(string Path, string Text)>.Builder result = ImmutableArray.CreateBuilder<(string Path, string Text)>();
        foreach (SyntaxTree tree in trees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SyntaxNode root = new Qualifier(compilation.GetSemanticModel(tree), cancellationToken).Visit(tree.GetRoot(cancellationToken))!;
            result.Add((tree.FilePath, root.ToFullString()));
        }
        ImmutableArray<(string Path, string Text)> qualified = result.ToImmutable();
        var verified = compilation.RemoveAllSyntaxTrees().AddSyntaxTrees(qualified.Select(source => CSharpSyntaxTree.ParseText(source.Text,
            new CSharpParseOptions(LanguageVersion.CSharp14), source.Path, cancellationToken: cancellationToken)));
        Diagnostic[] rewrittenErrors = verified.GetDiagnostics(cancellationToken).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        if (rewrittenErrors.Length != 0) throw new InvalidOperationException("Qualified runtime sources failed contract validation: " + string.Join("; ", rewrittenErrors.Select(error => error.ToString())));
        return qualified;
    }

    private sealed class Qualifier(
        SemanticModel model,
        CancellationToken cancellationToken
    ) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            if (IsRightHandName(node)) return node;
            string? name = FrameworkName(node);
            return name is null ? base.VisitIdentifierName(node) : SyntaxFactory.ParseName(name).WithTriviaFrom(node);
        }

        public override SyntaxNode? VisitGenericName(GenericNameSyntax node)
        {
            if (IsRightHandName(node)) return base.VisitGenericName(node);
            string? name = FrameworkName(node);
            return name is null ? base.VisitGenericName(node) : SyntaxFactory.ParseName(name).WithTriviaFrom(node);
        }

        public override SyntaxNode? VisitQualifiedName(QualifiedNameSyntax node)
        {
            if (IsRightHandName(node)) return base.VisitQualifiedName(node);
            string? name = FrameworkName(node);
            return name is null ? base.VisitQualifiedName(node) : SyntaxFactory.ParseName(name).WithTriviaFrom(node);
        }

        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            string? name = FrameworkName(node);
            return name is null ? base.VisitMemberAccessExpression(node) : SyntaxFactory.ParseExpression(name).WithTriviaFrom(node);
        }

        private string? FrameworkName(SyntaxNode node)
        {
            ISymbol? symbol = model.GetSymbolInfo(node, cancellationToken).Symbol;
            string? ns = symbol is INamespaceSymbol space ? space.ToDisplayString() : symbol is INamedTypeSymbol type ? type.ContainingNamespace.ToDisplayString() : null;
            if (ns is null || ns != "System" && !ns.StartsWith("System.", StringComparison.Ordinal)) return null;
            if (symbol is INamedTypeSymbol { IsNativeIntegerType: true } nativeInteger)
                return nativeInteger.SpecialType == SpecialType.System_IntPtr ? "global::System.IntPtr" : "global::System.UIntPtr";
            SymbolDisplayFormat format = SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions & ~SymbolDisplayMiscellaneousOptions.UseSpecialTypes);
            string name = symbol!.ToDisplayString(format);
            return name.StartsWith("global::", StringComparison.Ordinal) ? name : "global::" + name;
        }

        private static bool IsRightHandName(SyntaxNode node) => node.Parent is QualifiedNameSyntax qualified && qualified.Right == node
            || node.Parent is MemberAccessExpressionSyntax access && access.Name == node
            || node.Parent is AliasQualifiedNameSyntax alias && alias.Name == node;
    }
}
