using System;
using System.Collections.Generic;
using System.Linq;
using BGCS.CSharp.Emission;
using BGCS.CSharp.Planning;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BGCS.CSharp.Validation;

internal static class NullDefaultContractVerifier
{
    internal static void Verify(
        CSharpEmissionPlan plan,
        IEnumerable<ClassDeclarationSyntax> contexts
    ) {
        MethodDeclarationSyntax[] actual = contexts.SelectMany(context => context.Members.OfType<MethodDeclarationSyntax>()).ToArray();
        foreach (CSharpNullDefaultMethodPlan operation in plan.nullDefaultMethods)
        {
            MethodDeclarationSyntax expected = (MethodDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(NullDefaultMethodEmitter.Emit(plan, operation))!;
            MethodDeclarationSyntax[] matching = actual.Where(method => method.Identifier.ValueText == expected.Identifier.ValueText
                && SyntaxFactory.AreEquivalent(method.ParameterList, expected.ParameterList)).ToArray();
            if (matching.Length != 1 || !SyntaxFactory.AreEquivalent(matching[0], expected))
                throw new InvalidOperationException("A NULL-only operation changed its exact constant pointer, checked input conversions, native call or original-storage lifetime.");
            CSharpFacadeMethodPlan[] allowed = new[] { operation.signature }.Concat(plan.defaultOverloads.Select(overload => overload.signature))
                .Concat(plan.inputVariations.Select(variation => variation.signature)).Where(signature => signature.nativeExport == operation.signature.nativeExport
                    && signature.name == operation.signature.name).ToArray();
            foreach (MethodDeclarationSyntax method in actual.Where(method => method.Identifier.ValueText == expected.Identifier.ValueText
                && method.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PublicKeyword))
                && method.ParameterList.Parameters.Any(parameter => parameter.Type?.DescendantNodesAndSelf()
                    .Any(type => type is PointerTypeSyntax or FunctionPointerTypeSyntax) == true)))
                if (!allowed.Any(signature => SyntaxFactory.AreEquivalent(method.ParameterList,
                    ((MethodDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration("public void " + signature.name + "(" + string.Join(", ", Enumerable.Range(0, signature.parameterTypes.Length)
                        .Select(index => FacadeParameters.Declaration(signature, index))) + ") {}")!).ParameterList)))
                    throw new InvalidOperationException("A NULL-only operation acquired an unplanned pointer-bearing public overload; the complete pointer form must remain in Raw.");
        }
    }
}
