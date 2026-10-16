using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BGCS.CSharp.Emission;

/// <summary>Rewrites runtime source names independently of either binding model or emitter entry point.</summary>
internal static class RuntimeSourceNamespaceRewriter
{
    internal static string Rewrite(
        string source,
        string targetNamespace
    ) {
        if (targetNamespace == "BGCS.Runtime") return source;
        CompilationUnitSyntax syntax = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
        return new Rewriter(targetNamespace).Visit(syntax)!.ToFullString();
    }

    private sealed class Rewriter(string targetNamespace) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitQualifiedName(QualifiedNameSyntax node)
        {
            string text = node.ToString();
            string prefix = text.StartsWith("global::", StringComparison.Ordinal) ? "global::" : string.Empty;
            string name = text[prefix.Length..];
            if (name == "BGCS.Runtime" || name.StartsWith("BGCS.Runtime.", StringComparison.Ordinal))
                return SyntaxFactory.ParseName(prefix + targetNamespace + name["BGCS.Runtime".Length..]).WithTriviaFrom(node);
            if (prefix.Length == 0 && name.StartsWith("System.", StringComparison.Ordinal))
                return SyntaxFactory.ParseName("global::" + name).WithTriviaFrom(node);
            return base.VisitQualifiedName(node);
        }

        public override SyntaxNode? VisitUsingDirective(UsingDirectiveSyntax node)
        {
            if (node.Name is NameSyntax name && (name.ToString() == "System" || name.ToString().StartsWith("System.", StringComparison.Ordinal)))
                return node.WithName(SyntaxFactory.ParseName("global::" + name).WithTriviaFrom(name));
            return base.VisitUsingDirective(node);
        }
    }
}
