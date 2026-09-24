namespace BGCS.Emission;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
/// Composes generated C# syntax trees into one deterministic compilation unit.
/// </summary>
public sealed class SingleFileComposer
{
    public string Compose(IEnumerable<string> sourceFiles, string outputFile, string targetNamespace,
        string? abiReferenceTarget = null)
    {
        ArgumentNullException.ThrowIfNull(sourceFiles);
        if (string.IsNullOrWhiteSpace(outputFile))
            throw new ArgumentException("An output file is required.", nameof(outputFile));
        if (string.IsNullOrWhiteSpace(targetNamespace))
            throw new ArgumentException("A target namespace is required.", nameof(targetNamespace));

        return ComposeSources(sourceFiles.Select(path => (Path: path, Text: File.ReadAllText(path))),
            outputFile, targetNamespace, abiReferenceTarget);
    }

    public string ComposeSources(IEnumerable<(string Path, string Text)> sources, string outputFile,
        string targetNamespace, string? abiReferenceTarget = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        Dictionary<string, UsingDirectiveSyntax> usings = new(StringComparer.Ordinal);
        Dictionary<string, UsingDirectiveSyntax> namespaceUsings = new(StringComparer.Ordinal);
        List<AttributeListSyntax> attributes = [];
        List<MemberDeclarationSyntax> namespaceMembers = [];
        List<MemberDeclarationSyntax> otherMembers = [];
        bool nullableEnabled = false;
        foreach ((string sourceFile, string sourceText) in sources.OrderBy(source => source.Path, StringComparer.OrdinalIgnoreCase))
        {
            SyntaxTree tree = CSharpSyntaxTree.ParseText(sourceText, path: sourceFile);
            Diagnostic[] errors = tree.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
            if (errors.Length > 0)
                throw new InvalidOperationException($"Cannot compose invalid generated source '{sourceFile}':{Environment.NewLine}{FormatParsingErrors(sourceText, errors)}");
            CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
            nullableEnabled |= root.DescendantTrivia(descendIntoTrivia: true)
                .Select(trivia => trivia.GetStructure())
                .OfType<NullableDirectiveTriviaSyntax>()
                .Any(directive => directive.SettingToken.IsKind(SyntaxKind.EnableKeyword));
            foreach (UsingDirectiveSyntax directive in root.Usings)
                usings.TryAdd(directive.WithoutTrivia().ToFullString(), directive.WithoutTrivia());
            attributes.AddRange(root.AttributeLists.Select(attribute => attribute.WithoutTrivia()));
            foreach (MemberDeclarationSyntax member in root.Members)
            {
                if (member is BaseNamespaceDeclarationSyntax ns && ns.Name.ToString() == targetNamespace)
                {
                    foreach (UsingDirectiveSyntax directive in ns.Usings)
                    {
                        UsingDirectiveSyntax normalizedDirective = directive.WithoutTrivia();
                        Dictionary<string, UsingDirectiveSyntax> target = directive.Alias == null ? usings : namespaceUsings;
                        target.TryAdd(normalizedDirective.ToFullString(), normalizedDirective);
                    }
                    namespaceMembers.AddRange(ns.Members);
                }
                else
                {
                    otherMembers.Add(member);
                }
            }
        }

        CompilationUnitSyntax output = SyntaxFactory.CompilationUnit()
            .WithAttributeLists(SyntaxFactory.List(attributes))
            .WithUsings(SyntaxFactory.List(usings.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value)));
        List<MemberDeclarationSyntax> members = [];
        if (namespaceMembers.Count > 0)
        {
            members.Add(SyntaxFactory.NamespaceDeclaration(SyntaxFactory.ParseName(targetNamespace))
                .WithUsings(SyntaxFactory.List(namespaceUsings.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value)))
                .WithMembers(SyntaxFactory.List(namespaceMembers)));
        }
        members.AddRange(otherMembers);
        output = output.WithMembers(SyntaxFactory.List(members));
        string normalized = output.NormalizeWhitespace("    ", Environment.NewLine).ToFullString()
            .Replace("> )", ">)", StringComparison.Ordinal);
        string nullableDirective = nullableEnabled ? $"#nullable enable{Environment.NewLine}" : string.Empty;
        string text = CreateHeader(abiReferenceTarget) + nullableDirective + normalized + Environment.NewLine;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputFile))!);
        File.WriteAllText(outputFile, text);
        return outputFile;
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
        return header.ToString();
    }

    private static string FormatParsingErrors(string sourceText, IReadOnlyList<Diagnostic> errors)
    {
        string[] lines = sourceText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        return string.Join(Environment.NewLine, errors.Select(error =>
        {
            int line = error.Location.GetLineSpan().StartLinePosition.Line;
            string excerpt = line >= 0 && line < lines.Length ? lines[line].TrimEnd() : string.Empty;
            return excerpt.Length == 0
                ? error.ToString()
                : $"{error}{Environment.NewLine}    {excerpt}";
        }));
    }
}
