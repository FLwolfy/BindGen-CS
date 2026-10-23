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

/// <summary>Protects temporary text, synchronous pins, validated count copies and reference forwarding after processing.</summary>
internal static class BufferValueContractVerifier
{
    internal static void Verify(
        CSharpEmissionPlan plan,
        string libraryName,
        IEnumerable<ClassDeclarationSyntax> contexts
    ) {
        CSharpMethodPlan[] protocolMethods = plan.methods.Where(method => !method.bufferProtocols.IsEmpty || !method.valueParameters.IsEmpty || !method.stringInputs.IsEmpty).ToArray();
        if (protocolMethods.Length == 0) return;
        HashSet<string> names = protocolMethods.Select(method => method.name.TrimStart('@')).ToHashSet(StringComparer.Ordinal);
        MemberDeclarationSyntax[] actual = contexts.SelectMany(context => context.Members).ToArray();
        EmissionRequest request = new(libraryName, new InspectionSink())
        {
            importMode = plan.importMode,
            runtimeNamespace = plan.runtimeNamespace,
            managedRuntime = plan.managedRuntime,
            staticExportTable = plan.staticExportTable
        };
        ClassDeclarationSyntax expected = Parse(FriendlyApiEmitter.Emit(plan, request));
        foreach (MethodDeclarationSyntax method in expected.Members.OfType<MethodDeclarationSyntax>().Where(method => names.Contains(method.Identifier.ValueText)))
            Check(method);

        // Object dispatch has a private helper as well as a public reference facade. Both are part of its lease boundary.
        if (plan.statusMethods.Any(status => status.instanceIndex >= 0 && protocolMethods.Contains(plan.methods[status.methodIndex])))
            foreach (MemberDeclarationSyntax member in Parse(OwnedObjectEmitter.Emit(plan, request)).Members) Check(member);
        // An owned factory may add a private destructor helper beside the public protocol method.
        foreach (CSharpStatusMethodPlan status in plan.statusMethods.Where(status => status.instanceIndex < 0 && status.ownedResult is not null
            && protocolMethods.Contains(plan.methods[status.methodIndex])))
            foreach (MemberDeclarationSyntax member in Parse("public class Inspection {" + OwnedResultEmitter.Emit(plan, request, status) + "}").Members)
                Check(member);

        void Check(MemberDeclarationSyntax member)
        {
            MemberDeclarationSyntax[] candidates = member switch
            {
                MethodDeclarationSyntax method => actual.OfType<MethodDeclarationSyntax>().Where(candidate => candidate.Identifier.ValueText == method.Identifier.ValueText
                    && SyntaxFactory.AreEquivalent(candidate.ParameterList, method.ParameterList)).Cast<MemberDeclarationSyntax>().ToArray(),
                ClassDeclarationSyntax type => actual.OfType<ClassDeclarationSyntax>().Where(candidate => candidate.Identifier.ValueText == type.Identifier.ValueText)
                    .Cast<MemberDeclarationSyntax>().ToArray(),
                _ => throw new InvalidOperationException("An unexpected member entered the synchronous buffer/value protocol.")
            };
            if (candidates.Length != 1 || !SyntaxFactory.AreEquivalent(candidates[0], member))
                throw new InvalidOperationException("A synchronous text/buffer/value projection changed its temporary allocator, checked encoding, reference, alignment, count, copy or native lifetime contract.");
        }
    }

    private static ClassDeclarationSyntax Parse(string source) => (ClassDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(source)!;
    private sealed class InspectionSink : IArtifactSink
    {
        public string WriteText(
            string relativePath,
            string content
        ) => throw new InvalidOperationException("Protocol inspection must not write artifacts.");
    }
}
