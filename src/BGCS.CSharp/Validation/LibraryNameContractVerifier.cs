using System;
using System.Collections.Generic;
using System.Linq;
using BGCS.CSharp.Emission;
using BGCS.CSharp.Planning;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BGCS.CSharp.Validation;

internal static class LibraryNameContractVerifier
{
    internal static void Verify(
        CSharpEmissionPlan plan,
        string libraryName,
        IEnumerable<CompilationUnitSyntax> roots,
        IEnumerable<ClassDeclarationSyntax> contexts
    ) {
        Check(contexts);
        if (plan.sharedContext is { } shared)
        {
            string expected = string.Join(".", plan.api.@namespace.Split('.').Select(part => part.TrimStart('@'))) + "." + shared.name.TrimStart('@');
            Check(roots.SelectMany(root => root.DescendantNodes().OfType<ClassDeclarationSyntax>()).Where(type => FullName(type) == expected));
        }

        void Check(IEnumerable<ClassDeclarationSyntax> declarations)
        {
            MemberDeclarationSyntax[] matches = declarations.SelectMany(type => type.Members).Where(member => member switch
            {
                FieldDeclarationSyntax field => field.Declaration.Variables.Any(variable => variable.Identifier.ValueText == LibraryNameEmitter.C_NAME),
                PropertyDeclarationSyntax property => plan.api.emitLibraryNameConstant && property.Identifier.ValueText == LibraryNameEmitter.C_NAME,
                MethodDeclarationSyntax method => plan.api.emitLibraryNameConstant && method.Identifier.ValueText == LibraryNameEmitter.C_NAME,
                BaseTypeDeclarationSyntax type => plan.api.emitLibraryNameConstant && type.Identifier.ValueText == LibraryNameEmitter.C_NAME,
                DelegateDeclarationSyntax declaration => plan.api.emitLibraryNameConstant && declaration.Identifier.ValueText == LibraryNameEmitter.C_NAME,
                _ => false
            }).ToArray();
            if (plan.api.emitLibraryNameConstant
                ? matches.Length != 1 || !SyntaxFactory.AreEquivalent(matches[0], SyntaxFactory.ParseMemberDeclaration(LibraryNameEmitter.Member(libraryName)))
                : matches.Length != 0)
                throw new InvalidOperationException("The public native library name constant changed its configured presence, exact identity, type, visibility or value.");
        }
    }

    private static string FullName(ClassDeclarationSyntax type)
        => string.Join(".", type.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(scope => scope.Name.ToString().Replace("@", string.Empty, StringComparison.Ordinal))
            .Concat(type.Ancestors().OfType<TypeDeclarationSyntax>().Reverse().Select(parent => parent.Identifier.ValueText)).Append(type.Identifier.ValueText));
}
