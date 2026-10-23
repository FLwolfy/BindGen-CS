using System;
using System.Collections.Generic;
using System.Linq;
using BGCS.CSharp.Planning;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BGCS.CSharp.Validation;

internal static class ImportLibraryNameContractVerifier
{
    internal static void Verify(
        CSharpEmissionPlan plan,
        string libraryName,
        IEnumerable<CompilationUnitSyntax> roots
    ) {
        if (plan.importLibraryName is not { } constant)
        {
            if (plan.api.importLibraryNameConstant is not null)
                throw new InvalidOperationException("The external native library constant has no compiler-established import plan.");
            return;
        }
        if (constant.value != libraryName || !constant.reference.StartsWith("global::" + constant.declaringType + ".", StringComparison.Ordinal)
            || plan.api.importLibraryNameConstant is null || plan.importMode == BGCS.Core.BindingImportMode.FunctionTable)
            throw new InvalidOperationException("The external native library constant changed its planned import identity.");
        foreach (MemberDeclarationSyntax declaration in roots.SelectMany(root => root.DescendantNodes().OfType<MemberDeclarationSyntax>()))
        {
            string? name = declaration switch
            {
                BaseTypeDeclarationSyntax type => type.Identifier.ValueText,
                DelegateDeclarationSyntax type => type.Identifier.ValueText,
                _ => null
            };
            if (name is null) continue;
            string qualified = string.Join(".", declaration.Ancestors().Reverse().SelectMany(ancestor => ancestor switch
            {
                BaseNamespaceDeclarationSyntax ns => ns.Name.DescendantTokens().Where(token => token.IsKind(SyntaxKind.IdentifierToken)).Select(token => token.ValueText),
                TypeDeclarationSyntax type => new[] { type.Identifier.ValueText },
                _ => Array.Empty<string>()
            }).Append(name));
            string owner = constant.declaringType.Replace("@", string.Empty, StringComparison.Ordinal);
            if (owner == qualified || owner.StartsWith(qualified + ".", StringComparison.Ordinal))
                throw new InvalidOperationException("A generated artifact shadows the frozen external native library constant declaring type.");
        }
    }

    internal static bool Matches(
        CSharpEmissionPlan plan,
        ExpressionSyntax expression,
        string libraryName
    )
        => plan.importLibraryName is { } constant
            ? SyntaxFactory.AreEquivalent(expression, SyntaxFactory.ParseExpression(constant.reference))
            : expression is LiteralExpressionSyntax literal && literal.Token.Value is string value && value == libraryName;
}
