using System;
using System.Collections.Generic;
using System.Linq;
using BGCS.CSharp.Emission;
using BGCS.CSharp.Planning;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BGCS.CSharp.Validation;

internal static class PointerCarrierContractVerifier
{
    internal static void Verify(
        CSharpEmissionPlan plan,
        IEnumerable<CompilationUnitSyntax> roots,
        IEnumerable<ClassDeclarationSyntax> contexts
    ) {
        foreach (CSharpPointerCarrierPlan carrier in plan.pointerCarriers)
        {
            string expectedName = CSharpGeneratedTypeScope.DeclarationNamespace(plan.api).Replace("@", string.Empty, StringComparison.Ordinal) + "." + carrier.name.TrimStart('@');
            StructDeclarationSyntax[] actual = roots.SelectMany(root => root.DescendantNodes().OfType<StructDeclarationSyntax>()).Where(type => Name(type) == expectedName).ToArray();
            MemberDeclarationSyntax expected = SyntaxFactory.ParseMemberDeclaration(PointerCarrierEmitter.Emit(plan, carrier))!;
            if (actual.Length != 1 || !SyntaxFactory.AreEquivalent(actual[0], expected))
                throw new InvalidOperationException("A non-owning pointer carrier changed its exact address storage, qualification, unsafe extent or field-view contract.");
        }
        foreach (CSharpPointerFunctionPlan operation in plan.pointerFunctions)
        {
            ClassDeclarationSyntax reference = (ClassDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration("class Inspection {" + PointerFunctionEmitter.Emit(plan, operation) + "}")!;
            MethodDeclarationSyntax expected = reference.Members.OfType<MethodDeclarationSyntax>().Single();
            MethodDeclarationSyntax[] actual = contexts.SelectMany(context => context.Members).OfType<MethodDeclarationSyntax>()
                .Where(method => method.Identifier.ValueText == expected.Identifier.ValueText && SyntaxFactory.AreEquivalent(method.ParameterList, expected.ParameterList)).ToArray();
            if (actual.Length != 1 || !SyntaxFactory.AreEquivalent(actual[0], expected))
                throw new InvalidOperationException("An address function changed its exact raw conversion, temporary lifetime, module call lease or non-owning result.");
        }
    }

    private static string Name(StructDeclarationSyntax type) => string.Join(".", type.Ancestors().Reverse().SelectMany(node => node switch
    {
        BaseNamespaceDeclarationSyntax ns => new[] { ns.Name.ToString().Replace("@", string.Empty, StringComparison.Ordinal) },
        TypeDeclarationSyntax parent => new[] { parent.Identifier.ValueText },
        _ => Array.Empty<string>()
    }).Append(type.Identifier.ValueText));
}
