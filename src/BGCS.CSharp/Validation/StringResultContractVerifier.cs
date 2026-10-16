using System;
using System.Collections.Generic;
using System.Linq;
using BGCS.CSharp.Emission;
using BGCS.CSharp.Planning;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BGCS.CSharp.Validation;

internal static class StringResultContractVerifier
{
    internal static void Verify(
        CSharpEmissionPlan plan,
        IEnumerable<ClassDeclarationSyntax> contexts
    ) {
        MethodDeclarationSyntax[] actual = contexts.SelectMany(context => context.Members).OfType<MethodDeclarationSyntax>().ToArray();
        foreach (CSharpStringResultPlan result in plan.stringResults)
        {
            Check(StringResultEmitter.Emit(plan, result));
        }
        foreach (CSharpValueStringResultPlan result in plan.valueStringResults)
        {
            Check(ValueStringResultEmitter.Emit(plan, result));
            if (result.receiver is not null) Check(ValueStringResultEmitter.EmitInstanceHelper(plan, result));
        }
        void Check(string source)
        {
            ClassDeclarationSyntax reference = (ClassDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration("class Inspection {" + source + "}")!;
            MethodDeclarationSyntax expected = reference.Members.OfType<MethodDeclarationSyntax>().Single();
            MethodDeclarationSyntax[] matches = actual.Where(method => method.Identifier.ValueText == expected.Identifier.ValueText
                && SyntaxFactory.AreEquivalent(method.ParameterList, expected.ParameterList)).ToArray();
            if (matches.Length != 1 || !SyntaxFactory.AreEquivalent(expected, matches[0]))
                throw new InvalidOperationException("A native string result changed its exact encoding, extent/null checks, active module call lease or matching cleanup contract.");
        }
    }
}
