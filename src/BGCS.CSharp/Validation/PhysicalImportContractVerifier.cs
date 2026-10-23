using System;
using System.Collections.Generic;
using System.Linq;
using BGCS.Core.Artifacts;
using BGCS.Core.Requests;
using BGCS.CSharp.Emission;
using BGCS.CSharp.Planning;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BGCS.CSharp.Validation;

internal static class PhysicalImportContractVerifier
{
    internal static void Verify(
        CSharpEmissionPlan plan,
        ClassDeclarationSyntax[] contexts,
        string libraryName
    ) {
        CSharpPhysicalImportPlan[] expected;
        try { expected = PhysicalImportPlanner.Plan(plan).ToArray(); }
        catch (Exception failure) when (failure is NotSupportedException or ArgumentException)
        { throw new InvalidOperationException("The physical import transport has no valid runtime capability.", failure); }
        if (plan.physicalImports.IsDefault || expected.Length != plan.physicalImports.Length
            || expected.Where((
                item,
                index
            ) => !Matches(item, plan.physicalImports[index])).Any())
            throw new InvalidOperationException("The physical native import carriers or exact function-address conversions changed their runtime-established plan.");
        if (expected.Length == 0) return;

        // The native signature alone is insufficient: preserve the exact public
        // typed raw method, the private address conversion, and the installed table.
        ClassDeclarationSyntax raw = (ClassDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(RawApiEmitter.Emit(plan))!;
        CheckMembers(raw.Members.OfType<ClassDeclarationSyntax>().Single().Members,
            contexts.SelectMany(context => context.Members.OfType<ClassDeclarationSyntax>()).Where(type => type.Identifier.ValueText == "RawApi").SelectMany(type => type.Members));
        ClassDeclarationSyntax context = (ClassDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(ContextEmitter.Emit(plan, libraryName))!;
        CheckMembers(context.Members, contexts.SelectMany(type => type.Members));
        if (plan.importMode != BGCS.Core.BindingImportMode.FunctionTable)
        {
            ClassDeclarationSyntax imports = (ClassDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(NativeImportEmitter.Emit(plan, new EmissionRequest(libraryName, new Sink())))!;
            CheckMembers(imports.Members.OfType<ClassDeclarationSyntax>().Single().Members,
                contexts.SelectMany(type => type.Members.OfType<ClassDeclarationSyntax>()).Where(type => type.Identifier.ValueText == "NativeImports").SelectMany(type => type.Members), complete: true);
        }
    }

    private static bool Matches(
        CSharpPhysicalImportPlan first,
        CSharpPhysicalImportPlan second
    )
        => first.methodIndex == second.methodIndex && first.declarationName == second.declarationName && first.fieldName == second.fieldName
            && first.returnType == second.returnType && first.parameterTypes.SequenceEqual(second.parameterTypes)
            && first.addressParameters.SequenceEqual(second.addressParameters) && first.addressResult == second.addressResult;

    private static void CheckMembers(
        IEnumerable<MemberDeclarationSyntax> expected,
        IEnumerable<MemberDeclarationSyntax> actual,
        bool complete = false
    ) {
        MemberDeclarationSyntax[] expectedMembers = expected.ToArray(), actualMembers = actual.ToArray();
        if (complete && expectedMembers.Length != actualMembers.Length)
            throw new InvalidOperationException("The physical import owner has undeclared or missing forwarding members.");
        ILookup<string, MemberDeclarationSyntax> members = actualMembers.ToLookup(Key, StringComparer.Ordinal);
        foreach (MemberDeclarationSyntax member in expectedMembers)
        {
            MemberDeclarationSyntax[] matches = members[Key(member)].ToArray();
            if (matches.Length != 1 || !SyntaxFactory.AreEquivalent(member, matches[0]))
                throw new InvalidOperationException("A physical import changed its typed raw call, exact address cast, physical signature, table installation or module lifetime: " + Key(member));
        }
    }

    private static string Key(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax method => "method:" + method.Identifier.ValueText + "(" + string.Join(",", method.ParameterList.Parameters.Select(parameter => parameter.Type!.WithoutTrivia().NormalizeWhitespace().ToFullString())) + ")",
        ConstructorDeclarationSyntax constructor => "constructor:" + constructor.Identifier.ValueText,
        FieldDeclarationSyntax field => "field:" + string.Join(",", field.Declaration.Variables.Select(variable => variable.Identifier.ValueText)),
        PropertyDeclarationSyntax property => "property:" + property.Identifier.ValueText,
        BaseTypeDeclarationSyntax type => "type:" + type.Identifier.ValueText,
        _ => member.WithoutTrivia().NormalizeWhitespace().ToFullString()
    };

    private sealed class Sink : IArtifactSink
    {
        public string WriteText(
            string relativePath,
            string content
        ) => throw new InvalidOperationException("Import inspection cannot write artifacts.");
    }
}
