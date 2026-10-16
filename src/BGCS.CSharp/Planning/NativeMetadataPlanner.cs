using System;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Contracts.Interop;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BGCS.CSharp.Planning;

internal static class NativeMetadataPlanner
{
    internal static ImmutableDictionary<string, NativeCallMetadata> Plan(CSharpEmissionPlan plan)
    {
        if (!plan.api.emitNativeMetadata)
            return ImmutableDictionary<string, NativeCallMetadata>.Empty;
        string context = string.Join(".", plan.api.@namespace.Split('.').Select(part => part.TrimStart('@'))) + "." + plan.contextName.TrimStart('@');
        ImmutableDictionary<string, NativeCallMetadata>.Builder result = ImmutableDictionary.CreateBuilder<string, NativeCallMetadata>(StringComparer.Ordinal);
        for (int index = 0; index < plan.methods.Length; index++)
        {
            CSharpMethodPlan method = plan.methods[index];
            InteropFunction function = plan.api.interop.functions.Single(function => function.id == method.nativeExport);
            if (function.parameters.Length != method.parameterTypes.Length)
                throw new InvalidOperationException("Native metadata cannot infer a different raw parameter mapping.");
            NativeCallMetadata metadata = new(function.sourceQualifiedName ?? function.sourceName ?? function.exportName,
                plan.api.interop.GetType(function.returnType).nativeName,
                function.parameters.Select(parameter => new NativeParameterMetadata(parameter.sourceName ?? parameter.name,
                    plan.api.interop.GetType(parameter.type).nativeName)).ToImmutableArray());
            result.Add(Key(context + ".RawApi", method.name.TrimStart('@'), method.parameterTypes), metadata);
            if (plan.importMode != BGCS.Contracts.BindingImportMode.FunctionTable)
            {
                CSharpPhysicalImportPlan? physical = plan.physicalImports.SingleOrDefault(import => import.methodIndex == index);
                result.Add(Key(context + ".NativeImports", physical?.declarationName ?? "Function" + index,
                    physical?.parameterTypes ?? method.parameterTypes), metadata);
            }
        }
        return result.ToImmutable();
    }

    internal static string Key(MethodDeclarationSyntax method)
    {
        string owner = string.Join(".", method.Ancestors().Reverse().Select(node => node switch
        {
            BaseNamespaceDeclarationSyntax ns => string.Join(".", ns.Name.DescendantTokens().Where(token => token.IsKind(SyntaxKind.IdentifierToken)).Select(token => token.ValueText)),
            TypeDeclarationSyntax type => type.Identifier.ValueText,
            _ => null
        }).Where(name => name is not null));
        return owner + "." + method.Identifier.ValueText + "(" + string.Join(",", method.ParameterList.Parameters.Select(parameter => Compact(parameter.Type!))) + ")";
    }

    private static string Key(
        string owner,
        string name,
        ImmutableArray<string> parameters
    )
        => owner + "." + name + "(" + string.Join(",", parameters.Select(type => Compact(SyntaxFactory.ParseTypeName(type)))) + ")";
    private static string Compact(SyntaxNode node) => string.Concat(node.DescendantTokens().Select(token => token.Text));
}

internal sealed record NativeCallMetadata(
    string function,
    string returnType,
    ImmutableArray<NativeParameterMetadata> parameters
);
internal sealed record NativeParameterMetadata(
    string name,
    string type
);
