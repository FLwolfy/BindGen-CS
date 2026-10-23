using System;
using System.Collections.Generic;
using System.Linq;
using BGCS.Core.Artifacts;
using BGCS.Core.Requests;
using BGCS.CSharp.Emission;
using BGCS.CSharp.Planning;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BGCS.CSharp.Validation;

/// <summary>Protects factory cleanup, receiver identity, release domains and object call leases.</summary>
internal static class OwnedCProjectionContractVerifier
{
    internal static void Verify(
        CSharpEmissionPlan plan,
        string libraryName,
        IEnumerable<ClassDeclarationSyntax> contexts
    ) {
        if (plan.directOwnedFactories.IsEmpty && plan.directBorrowedFactories.IsEmpty && plan.objects.IsEmpty
            && !plan.statusMethods.Any(status => status.ownedResult is not null)) return;
        MemberDeclarationSyntax[] actual = contexts.SelectMany(context => context.Members).ToArray();
        foreach (CSharpDirectOwnedFactoryPlan factory in plan.directOwnedFactories)
            foreach (MemberDeclarationSyntax member in Parse("class Inspection {" + DirectOwnedFactoryEmitter.Emit(plan, factory) + "}").Members) Check(member);
        foreach (CSharpModuleBorrowedFactoryPlan factory in plan.directBorrowedFactories)
            foreach (MemberDeclarationSyntax member in Parse("class Inspection {" + ModuleBorrowedFactoryEmitter.Emit(plan, factory) + "}").Members) Check(member);
        foreach (CSharpReceiverMethodPlan member in plan.objects.SelectMany(item => item.directMethods))
            foreach (MemberDeclarationSyntax declaration in Parse("class Inspection {" + DirectReceiverEmitter.EmitContext(plan, member) + "}").Members) Check(declaration);
        EmissionRequest request = new(libraryName, new InspectionSink())
        {
            importMode = plan.importMode,
            runtimeNamespace = plan.runtimeNamespace,
            managedRuntime = plan.managedRuntime,
            staticExportTable = plan.staticExportTable
        };
        foreach (CSharpStatusMethodPlan status in plan.statusMethods.Where(status => status.instanceIndex < 0 && status.ownedResult is not null))
            foreach (MemberDeclarationSyntax member in Parse("class Inspection {" + OwnedResultEmitter.Emit(plan, request, status) + "}").Members) Check(member);
        // The complete wrapper protects its ownership fields and constructor as well as public member bodies.
        foreach (MemberDeclarationSyntax member in Parse(OwnedObjectEmitter.Emit(plan, request)).Members) Check(member);

        void Check(MemberDeclarationSyntax member)
        {
            MemberDeclarationSyntax[] matches = member switch
            {
                MethodDeclarationSyntax method => actual.OfType<MethodDeclarationSyntax>().Where(candidate => candidate.Identifier.ValueText == method.Identifier.ValueText
                    && SyntaxFactory.AreEquivalent(candidate.ParameterList, method.ParameterList)).Cast<MemberDeclarationSyntax>().ToArray(),
                ClassDeclarationSyntax type => actual.OfType<ClassDeclarationSyntax>().Where(candidate => candidate.Identifier.ValueText == type.Identifier.ValueText)
                    .Cast<MemberDeclarationSyntax>().ToArray(),
                _ => throw new InvalidOperationException("An unexpected member entered an ownership projection.")
            };
            if (matches.Length != 1 || !SyntaxFactory.AreEquivalent(matches[0], member))
                throw new InvalidOperationException("An ownership projection changed its exact receiver identity, allocation/release domain, module lease or matching cleanup contract.");
        }
    }

    private static ClassDeclarationSyntax Parse(string source) => (ClassDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(source)!;
    private sealed class InspectionSink : IArtifactSink
    {
        public string WriteText(
            string relativePath,
            string content
        ) => throw new InvalidOperationException("Ownership inspection must not write artifacts.");
    }
}
