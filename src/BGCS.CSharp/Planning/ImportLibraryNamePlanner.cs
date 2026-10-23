using System;
using System.Linq;
using System.Threading;
using BGCS.Core;
using BGCS.Core.Requests;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BGCS.CSharp.Planning;

internal static class ImportLibraryNamePlanner
{
    internal static CSharpEmissionPlan Apply(
        CSharpEmissionPlan plan,
        EmissionRequest request,
        CancellationToken cancellationToken
    ) {
        if (plan.api.importLibraryNameConstant is not { } reference) return plan;
        if (plan.importMode == BindingImportMode.FunctionTable)
            throw new ArgumentException("An import library constant requires DllImport or LibraryImport; FunctionTable accepts a module provider or explicit Load name.");
        NameSyntax name = SyntaxFactory.ParseName(reference);
        if (reference.Length > 2048 || name.ContainsDiagnostics || name.ToFullString() != reference
            || name.DescendantTrivia(descendIntoTrivia: true).Any()
            || name.DescendantNodesAndSelf().Any(node => node is GenericNameSyntax
                || node is AliasQualifiedNameSyntax alias && alias.Alias.Identifier.ValueText != "global")
            || name is not (QualifiedNameSyntax or AliasQualifiedNameSyntax))
            throw new ArgumentException("An import library constant requires a bounded qualified identifier, without statements, aliases or type arguments.");
        ManagedConstantCompilerInputs inputs = ManagedConstantCompilerInputs.Create(request.managedProjection,
            ConstantPresentationPlanner.frameworkReferences, cancellationToken);
        const string fieldName = "__BGCSImportLibraryConstant";
        string contextNamespace = CSharpNames.Namespace(plan.api.@namespace);
        string source = "#nullable enable\nnamespace " + contextNamespace + ";\npublic sealed unsafe partial class " + plan.contextName
            + " { private static partial class NativeImports { internal const string " + fieldName + " = " + reference + "; } }";
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, inputs.parseOptions, "bgcs-internal:/import-library-constant.g.cs", cancellationToken: cancellationToken);
        CSharpCompilation compilation = CSharpCompilation.Create("BGCS.ImportLibraryConstant.Validation", inputs.sources.Add(tree), inputs.references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true,
                generalDiagnosticOption: ReportDiagnostic.Error, nullableContextOptions: NullableContextOptions.Enable));
        inputs.VerifyAssemblyClosure(compilation, cancellationToken);
        Diagnostic[] failures = ManagedConstantCompilerInputs.ConstantFailures(compilation, tree, plan, cancellationToken);
        if (failures.Length != 0)
            throw new ArgumentException("The import library constant failed frozen warnings-as-errors compiler validation: "
                + string.Join("; ", failures.Select(failure => failure.ToString())));
        EqualsValueClauseSyntax initializer = tree.GetRoot(cancellationToken).DescendantNodes().OfType<VariableDeclaratorSyntax>()
            .Single(variable => variable.Identifier.ValueText == fieldName).Initializer!;
        SemanticModel semantics = compilation.GetSemanticModel(tree);
        if (semantics.GetSymbolInfo(initializer.Value, cancellationToken).Symbol is not IFieldSymbol field
            || !field.IsConst || field.Type.SpecialType != SpecialType.System_String
            || field.ConstantValue is not string value || value != request.libraryName)
            throw new ArgumentException("The import library constant must be one const string field whose value exactly matches the resolved native import name.");
        string owner = field.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        string ownerNamespace = field.ContainingNamespace.ToDisplayString();
        string generatedNamespace = string.Join(".", plan.api.@namespace.Split('.').Select(part => part.TrimStart('@')));
        if (ownerNamespace == generatedNamespace || ownerNamespace.StartsWith(generatedNamespace + ".", StringComparison.Ordinal))
            throw new ArgumentException("The import library constant must belong to a consumer namespace outside the generated namespace, so generated partial types cannot rebind its identity.");
        string canonical = owner + "." + CSharpNames.Identifier(field.Name);
        return plan with { importLibraryName = new(canonical, owner["global::".Length..], value) };
    }
}

/// <summary>Freezes a compiler-established consumer constant reference without granting native ABI evidence.</summary>
/// <param name="reference">The fully qualified const string field.</param>
/// <param name="declaringType">The exact external declaring type, without the global alias prefix.</param>
/// <param name="value">The independently checked resolved native import identity.</param>
public sealed record CSharpImportLibraryNamePlan(
    string reference,
    string declaringType,
    string value
);
