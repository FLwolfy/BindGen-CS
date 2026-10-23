using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace BGCS.CSharp.Emission;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
/// Composes generated C# syntax trees into one deterministic compilation unit.
/// </summary>
public sealed class SingleFileComposer
{
    /// <summary>
    /// Combines named source texts while deduplicating imports and preserving declaration syntax.
    /// </summary>
    /// <param name="sources">Named source text snapshots; paths provide ordering and diagnostic locations.</param>
    /// <param name="targetNamespace">The generated API namespace, validated before composition.</param>
    /// <param name="abiReferenceTarget">Optional native target identifier written in the generated header.</param>
    /// <param name="cancellationToken">Cancels parsing and composition before candidate publication.</param>
    /// <returns>The combined source text; the caller owns artifact publication.</returns>
    /// <exception cref="ArgumentNullException">The source sequence is null.</exception>
    /// <exception cref="InvalidOperationException">A source text contains syntax errors.</exception>
    public string ComposeSources(
        IEnumerable<(string Path, string Text)> sources,
        string targetNamespace,
        string? abiReferenceTarget = null,
        CancellationToken cancellationToken = default
    ) {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetNamespace);
        cancellationToken.ThrowIfCancellationRequested();
        targetNamespace = BGCS.CSharp.Planning.CSharpNames.Namespace(targetNamespace);
        Dictionary<string, UsingDirectiveSyntax> usings = new(StringComparer.Ordinal);
        List<AttributeListSyntax> attributes = [];
        List<MemberDeclarationSyntax> members = [];
        bool nullableEnabled = false;
        foreach ((string sourceFile, string sourceText) in sources.OrderBy(source => source.Path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            SyntaxTree tree = CSharpSyntaxTree.ParseText(sourceText, path: sourceFile, cancellationToken: cancellationToken);
            Diagnostic[] errors = tree.GetDiagnostics(cancellationToken).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
            if (errors.Length > 0)
                throw new InvalidOperationException($"Cannot compose invalid generated source '{sourceFile}':{Environment.NewLine}{FormatParsingErrors(sourceText, errors)}");
            CompilationUnitSyntax root = tree.GetCompilationUnitRoot(cancellationToken);
            nullableEnabled |= root.DescendantTrivia(descendIntoTrivia: true).Select(trivia => trivia.GetStructure()).OfType<NullableDirectiveTriviaSyntax>().Any(directive => directive.SettingToken.IsKind(SyntaxKind.EnableKeyword));
            foreach (UsingDirectiveSyntax directive in root.Usings)
                usings.TryAdd(directive.WithoutTrivia().ToFullString(), directive.WithoutTrivia());
            attributes.AddRange(root.AttributeLists.Select(attribute => attribute.WithoutTrivia()));
            foreach (MemberDeclarationSyntax originalMember in root.Members)
            {
                MemberDeclarationSyntax member = originalMember is FileScopedNamespaceDeclarationSyntax fileScoped
                    ? SyntaxFactory.NamespaceDeclaration(fileScoped.Name).WithExterns(fileScoped.Externs).WithUsings(fileScoped.Usings)
                        .WithMembers(fileScoped.Members).WithLeadingTrivia(fileScoped.GetLeadingTrivia()).WithTrailingTrivia(fileScoped.GetTrailingTrivia())
                    : originalMember;
                // Namespace-local aliases and imports belong to this declaration's lexical scope.
                // Combining them across partial declarations can silently bind a different type.
                members.Add(member);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        CompilationUnitSyntax output = SyntaxFactory.CompilationUnit().WithAttributeLists(SyntaxFactory.List(attributes)).WithUsings(SyntaxFactory.List(usings.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value)));
        output = output.WithMembers(SyntaxFactory.List(members));
        string normalized = GeneratedSourceFormatter.Normalize(output, Environment.NewLine);
        string nullableDirective = nullableEnabled ? $"#nullable enable{Environment.NewLine}" : string.Empty;
        string text = CreateHeader(abiReferenceTarget) + nullableDirective + normalized + Environment.NewLine;
        cancellationToken.ThrowIfCancellationRequested();
        return text;
    }

    private static string CreateHeader(string? abiReferenceTarget)
    {
        System.Text.StringBuilder header = new();
        header.AppendLine("// ------------------------------------------------------------------------------");
        header.AppendLine("// <auto-generated>");
        header.AppendLine("//     This code was generated by BindGen-CS.");
        if (!string.IsNullOrWhiteSpace(abiReferenceTarget))
            header.Append("//     ABI reference target: ").AppendLine(abiReferenceTarget);
        header.AppendLine("//     Changes will be replaced the next time bindings are generated.");
        header.AppendLine("// </auto-generated>");
        header.AppendLine("// ------------------------------------------------------------------------------");
        header.AppendLine("#pragma warning disable CS1591 // Native declarations may omit documentation.");
        return header.ToString();
    }

    private static string FormatParsingErrors(
        string sourceText,
        IReadOnlyList<Diagnostic> errors
    ) {
        string[] lines = sourceText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        return string.Join(Environment.NewLine, errors.Select(error =>
        {
            int line = error.Location.GetLineSpan().StartLinePosition.Line;
            string excerpt = line >= 0 && line < lines.Length ? lines[line].TrimEnd() : string.Empty;
            return excerpt.Length == 0 ? error.ToString() : $"{error}{Environment.NewLine}    {excerpt}";
        }));
    }
}
