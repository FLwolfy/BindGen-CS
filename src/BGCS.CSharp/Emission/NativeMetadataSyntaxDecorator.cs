using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using BGCS.CSharp.Planning;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BGCS.CSharp.Emission;

internal static class NativeMetadataSyntaxDecorator
{
    internal static void Apply(
        CSharpEmissionPlan plan,
        List<(string Path, string Text)> sources,
        CancellationToken cancellationToken
    ) {
        if (!plan.api.emitNativeMetadata)
            return;
        Rewriter rewriter = new(plan, verify: false);
        for (int index = 0; index < sources.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (string path, string text) = sources[index];
            CompilationUnitSyntax root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(text, new(LanguageVersion.CSharp14), path, cancellationToken: cancellationToken).GetRoot(cancellationToken);
            CompilationUnitSyntax annotated = (CompilationUnitSyntax)rewriter.Visit(root)!;
            if (!ReferenceEquals(root, annotated))
                sources[index] = (path, GeneratedSourceFormatter.Normalize(annotated, Environment.NewLine));
        }
        rewriter.Complete();
    }

    internal static List<CompilationUnitSyntax> VerifyAndRemove(
        CSharpEmissionPlan plan,
        List<CompilationUnitSyntax> roots
    ) {
        if (!plan.api.emitNativeMetadata)
        {
            foreach (CompilationUnitSyntax root in roots)
                foreach (AttributeSyntax attribute in root.DescendantNodes().OfType<AttributeSyntax>())
                    VerifyDisabledAttribute(attribute);
            return roots;
        }
        Rewriter rewriter = new(plan, verify: true);
        List<CompilationUnitSyntax> stripped = roots.Select(root => (CompilationUnitSyntax)rewriter.Visit(root)!).ToList();
        rewriter.Complete();
        return stripped;
    }

    // The final contract reader calls this for every actual attribute during its
    // complete syntax-index traversal, retaining the same disabled-mode guard
    // without a second traversal over all method bodies.
    internal static void VerifyDisabledAttribute(AttributeSyntax attribute)
    {
        if (IsMetadata(attribute))
            throw new InvalidOperationException("Native name attributes appeared outside their exact checked canonical signature or while metadata was disabled.");
    }

    private sealed class Rewriter(
        CSharpEmissionPlan plan,
        bool verify
    ) : CSharpSyntaxRewriter
    {
        private readonly ImmutableDictionary<string, NativeCallMetadata> m_expected = NativeMetadataPlanner.Plan(plan);
        private readonly HashSet<string> m_seen = new(StringComparer.Ordinal);

        internal void Complete()
        {
            if (!m_seen.SetEquals(m_expected.Keys))
                throw new InvalidOperationException("The generated native metadata is missing one or more canonical raw/import signatures.");
        }

        public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            string key = NativeMetadataPlanner.Key(node);
            if (!m_expected.TryGetValue(key, out NativeCallMetadata? metadata))
                return base.VisitMethodDeclaration(node);
            if (!m_seen.Add(key))
                throw new InvalidOperationException("Generated native metadata duplicates a canonical call signature.");
            SyntaxList<AttributeListSyntax> method = SyntaxFactory.List(new[] { Attribute("Func", metadata.function), Attribute("Type", metadata.returnType, returns: true) });
            MethodDeclarationSyntax rewritten;
            if (verify)
                rewritten = node.WithAttributeLists(VerifyAttributes(node.AttributeLists, method));
            else
            {
                SyntaxTriviaList leading = node.GetLeadingTrivia();
                rewritten = node.WithoutLeadingTrivia().AddAttributeLists([.. method]).WithLeadingTrivia(leading);
            }
            SeparatedSyntaxList<ParameterSyntax> parameters = rewritten.ParameterList.Parameters;
            for (int index = 0; index < parameters.Count; index++)
            {
                NativeParameterMetadata parameter = metadata.parameters[index];
                SyntaxList<AttributeListSyntax> attributes = SyntaxFactory.List(new[] { Attribute("Param", parameter.name), Attribute("Type", parameter.type) });
                ParameterSyntax actual = parameters[index];
                ParameterSyntax replacement = verify ? actual.WithAttributeLists(VerifyAttributes(actual.AttributeLists, attributes))
                    : actual.WithoutLeadingTrivia().AddAttributeLists([.. attributes]).WithLeadingTrivia(actual.GetLeadingTrivia());
                parameters = parameters.Replace(actual, replacement);
            }
            rewritten = rewritten.WithParameterList(rewritten.ParameterList.WithParameters(parameters));
            // Only verified metadata is removed before core ABI checks. Unknown
            // attributes and all native signatures/call bodies remain untouched.
            return verify ? base.VisitMethodDeclaration(rewritten) : rewritten;
        }

        public override SyntaxNode? VisitAttributeList(AttributeListSyntax node)
        {
            if (node.Attributes.Any(IsMetadata))
                throw new InvalidOperationException("Native name attributes appeared outside their exact checked canonical signature or while metadata was disabled.");
            return base.VisitAttributeList(node);
        }

        private static SyntaxList<AttributeListSyntax> VerifyAttributes(
            SyntaxList<AttributeListSyntax> actual,
            SyntaxList<AttributeListSyntax> expected
        ) {
            AttributeListSyntax[] metadata = actual.Where(list => list.Attributes.Any(IsMetadata)).ToArray();
            if (metadata.Length != expected.Count || metadata.Where((
                list,
                index
            ) => !SyntaxFactory.AreEquivalent(list, expected[index])).Any())
                throw new InvalidOperationException("Generated native metadata changed its exact original function, return type, parameter name/type, attribute identity or target.");
            return SyntaxFactory.List(actual.Where(list => !list.Attributes.Any(IsMetadata)));
        }

        private AttributeListSyntax Attribute(
            string kind,
            string value,
            bool returns = false
        ) {
            string runtime = "global::" + CSharpNames.Namespace(plan.runtimeNamespace);
            AttributeSyntax attribute = SyntaxFactory.Attribute(SyntaxFactory.ParseName(runtime + ".NativeNameAttribute"))
                .WithArgumentList(SyntaxFactory.AttributeArgumentList(SyntaxFactory.SeparatedList(new[]
                {
                    SyntaxFactory.AttributeArgument(SyntaxFactory.ParseExpression(runtime + ".NativeNameType." + kind)),
                    SyntaxFactory.AttributeArgument(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(value)))
                })));
            AttributeListSyntax list = SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attribute));
            return returns ? list.WithTarget(SyntaxFactory.AttributeTargetSpecifier(SyntaxFactory.Token(SyntaxKind.ReturnKeyword))) : list;
        }

    }

    private static bool IsMetadata(AttributeSyntax attribute)
    {
        string name = attribute.Name switch
        {
            IdentifierNameSyntax simple => simple.Identifier.ValueText,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax alias => alias.Name.Identifier.ValueText,
            _ => ""
        };
        return name is "NativeName" or "NativeNameAttribute";
    }
}
