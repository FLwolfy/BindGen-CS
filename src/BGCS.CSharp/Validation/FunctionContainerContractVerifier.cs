using System;
using System.Collections.Generic;
using System.Linq;
using BGCS.CSharp.Emission;
using BGCS.CSharp.Planning;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BGCS.CSharp.Validation;

internal static class FunctionContainerContractVerifier
{
    internal static void Verify(
        CSharpEmissionPlan plan,
        IReadOnlyList<CompilationUnitSyntax> roots,
        IEnumerable<ClassDeclarationSyntax> contexts
    ) {
        if (plan.functionContainers.IsEmpty) return;
        CompilationUnitSyntax expected = SyntaxFactory.ParseCompilationUnit(FunctionContainerEmitter.Emit(plan));
        var contextMembers = contexts.SelectMany(type => type.Members).ToArray();
        foreach (PropertyDeclarationSyntax property in expected.Members.OfType<ClassDeclarationSyntax>().Single().Members.OfType<PropertyDeclarationSyntax>())
        {
            PropertyDeclarationSyntax[] matches = contextMembers.OfType<PropertyDeclarationSyntax>().Where(member => member.Identifier.ValueText == property.Identifier.ValueText).ToArray();
            if (matches.Length != 1 || !SyntaxFactory.AreEquivalent(matches[0], property))
                throw new InvalidOperationException("A function container changed the binding context instance captured by its view.");
        }
        string ns = string.Join('.', plan.api.@namespace.Split('.').Select(part => part.TrimStart('@')));
        foreach (StructDeclarationSyntax view in expected.Members.OfType<StructDeclarationSyntax>())
        {
            StructDeclarationSyntax[] matches = roots.SelectMany(root => root.DescendantNodes().OfType<StructDeclarationSyntax>())
                .Where(type => FullName(type) == ns + "." + view.Identifier.ValueText).ToArray();
            if (matches.Length != 1 || !SyntaxFactory.AreEquivalent(matches[0], view))
                throw new InvalidOperationException("A function container changed its checked forwarding or context lifetime contract.");
        }
    }

    private static string FullName(BaseTypeDeclarationSyntax type) => string.Join('.', type.Ancestors().Reverse()
        .Select(node => node switch
        {
            BaseNamespaceDeclarationSyntax ns => ns.Name.ToString().Replace("@", "", StringComparison.Ordinal),
            BaseTypeDeclarationSyntax owner => owner.Identifier.ValueText,
            _ => null
        }).Where(value => value is not null).Append(type.Identifier.ValueText));
}
