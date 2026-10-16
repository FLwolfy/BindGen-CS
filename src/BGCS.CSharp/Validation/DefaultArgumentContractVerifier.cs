using System;
using System.Collections.Generic;
using System.Linq;
using BGCS.CSharp.Emission;
using BGCS.CSharp.Planning;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BGCS.CSharp.Validation;

internal static class DefaultArgumentContractVerifier
{
    internal static void Verify(
        CSharpEmissionPlan plan,
        IEnumerable<ClassDeclarationSyntax> contexts
    ) {
        if (plan.defaultOverloads.IsEmpty && plan.defaultConstants.IsEmpty) return;
        ClassDeclarationSyntax expected = (ClassDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(DefaultArgumentEmitter.Emit(plan))!;
        MemberDeclarationSyntax[] actual = contexts.SelectMany(type => type.Members).ToArray();
        foreach (MemberDeclarationSyntax member in expected.Members)
        {
            MemberDeclarationSyntax[] candidates = member switch
            {
                MethodDeclarationSyntax method => actual.OfType<MethodDeclarationSyntax>()
                    .Where(candidate => candidate.Identifier.ValueText == method.Identifier.ValueText
                        && SyntaxFactory.AreEquivalent(candidate.ParameterList, method.ParameterList)).Cast<MemberDeclarationSyntax>().ToArray(),
                ClassDeclarationSyntax type => actual.OfType<ClassDeclarationSyntax>().Where(candidate => candidate.Identifier.ValueText == type.Identifier.ValueText)
                    .Cast<MemberDeclarationSyntax>().ToArray(),
                _ => throw new InvalidOperationException("An unexpected member entered the finite default argument plan.")
            };
            if (candidates.Length != 1 || !SyntaxFactory.AreEquivalent(member, candidates[0]))
                throw new InvalidOperationException("A default argument overload changed its checked constant, full-argument forwarding or compile-time constant requirement.");
        }
    }
}
