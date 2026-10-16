using System;
using System.Collections.Generic;
using System.Linq;
using BGCS.CSharp.Emission;
using BGCS.CSharp.Planning;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BGCS.CSharp.Validation;

internal static class InputVariationContractVerifier
{
    internal static void Verify(
        CSharpEmissionPlan plan,
        IEnumerable<ClassDeclarationSyntax> contexts
    ) {
        if (plan.inputVariations.IsEmpty) return;
        var expected = (ClassDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(InputVariationEmitter.Emit(plan))!;
        MethodDeclarationSyntax[] actual = contexts.SelectMany(type => type.Members.OfType<MethodDeclarationSyntax>()).ToArray();
        foreach (MethodDeclarationSyntax method in expected.Members.OfType<MethodDeclarationSyntax>())
        {
            MethodDeclarationSyntax[] candidates = actual.Where(candidate => candidate.Identifier.ValueText == method.Identifier.ValueText
                && SyntaxFactory.AreEquivalent(candidate.ParameterList, method.ParameterList)).ToArray();
            if (candidates.Length != 1 || !SyntaxFactory.AreEquivalent(method, candidates[0]))
                throw new InvalidOperationException("An alternate managed input signature changed its checked conversion, original operation or exact argument order.");
        }
    }
}
