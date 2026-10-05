using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace BGCS.Tool.Validation;

internal static class CSharpStyleValidator
{
    internal static int Run(
        string[] arguments,
        TextWriter output,
        TextWriter error
    ) {
        string[] paths = arguments.Where(static argument => argument != "--fix").ToArray();
        if (paths.Length != 1 || arguments.Length != paths.Length + (arguments.Contains("--fix") ? 1 : 0))
        {
            error.WriteLine("Usage: bindgen-cs validate style <source-root> [--fix]");
            return 2;
        }

        string root = Path.GetFullPath(paths[0]);
        if (!Directory.Exists(root))
        {
            error.WriteLine($"Source root does not exist: {root}");
            return 2;
        }

        bool fix = arguments.Contains("--fix");
        int changed = 0;
        using var workspace = new AdhocWorkspace();
        foreach (string path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories).Where(static path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(static part => part is "bin" or "obj" or "extern" or "Generated")))
        {
            string source = File.ReadAllText(path);
            string formatted;
            try
            {
                formatted = Normalize(source, workspace);
            }
            catch (InvalidOperationException exception)
            {
                error.WriteLine($"{Path.GetRelativePath(root, path)}: {exception.Message}");
                return 2;
            }

            if (string.Equals(source.Replace("\r\n", "\n", StringComparison.Ordinal), formatted.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal))
                continue;
            changed++;
            if (fix)
                File.WriteAllText(path, formatted);
            else
                error.WriteLine($"{Path.GetRelativePath(root, path)}: declaration formatting differs from the development standard.");
        }

        output.WriteLine(fix ? $"Formatted {changed} source files with unchanged syntax tokens." : $"Style validation found {changed} files requiring formatting.");
        return fix || changed == 0 ? 0 : 1;
    }

    private static string Normalize(
        string source,
        Workspace workspace
    ) {
        SyntaxNode original = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview)).GetRoot();
        if (original.ContainsDiagnostics)
            throw new InvalidOperationException("Style normalization requires syntactically valid C# source: " + string.Join("; ", original.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
        var options = workspace.Options.WithChangedOption(FormattingOptions.NewLine, LanguageNames.CSharp, "\n")
            .WithChangedOption(FormattingOptions.IndentationSize, LanguageNames.CSharp, 4)
            .WithChangedOption(FormattingOptions.UseTabs, LanguageNames.CSharp, false);
        string[] before = SyntaxIdentity(original).ToArray();
        SyntaxNode current = original;
        string currentSource = source;
        for (int iteration = 0; iteration < 4; iteration++)
        {
            SyntaxNode standard = Formatter.Format(current, workspace, options);
            SyntaxNode formatted = new DeclarationRewriter().Visit(standard)!;
            string formattedSource = formatted.ToFullString().TrimEnd() + "\n";
            SyntaxNode reparsed = CSharpSyntaxTree.ParseText(formattedSource, new CSharpParseOptions(LanguageVersion.Preview)).GetRoot();
            if (reparsed.ContainsDiagnostics)
                throw new InvalidOperationException("Style normalization produced invalid source; no source was written.");
            if (!before.SequenceEqual(SyntaxIdentity(reparsed), StringComparer.Ordinal))
                throw new InvalidOperationException("Style normalization changed syntax tokens; no source was written.");
            if (string.Equals(currentSource, formattedSource, StringComparison.Ordinal))
                return formattedSource;
            current = reparsed;
            currentSource = formattedSource;
        }
        throw new InvalidOperationException("Style normalization did not converge; no source was written.");
    }

    private static IEnumerable<string> SyntaxIdentity(SyntaxNode root)
    {
        foreach (SyntaxToken token in root.DescendantTokens())
        {
            foreach (string trivia in ConditionalIdentity(token.LeadingTrivia))
                yield return trivia;
            yield return token.RawKind + ":" + token.Text;
            foreach (string trivia in ConditionalIdentity(token.TrailingTrivia))
                yield return trivia;
        }
    }

    private static IEnumerable<string> ConditionalIdentity(SyntaxTriviaList trivia)
    {
        foreach (SyntaxTrivia item in trivia)
        {
            if (item.IsKind(SyntaxKind.DisabledTextTrivia))
                yield return "disabled:" + item.ToFullString();
            else if (item.GetStructure() is DirectiveTriviaSyntax directive)
                yield return "directive:" + string.Join("|", directive.DescendantTokens()
                    .Select(static token => token.RawKind + ":" + token.Text));
        }
    }

    private sealed class DeclarationRewriter : CSharpSyntaxRewriter
    {
        /// <inheritdoc/>
        public override SyntaxNode? VisitParameterList(ParameterListSyntax node)
        {
            if (node.Parameters.Count <= 1)
                return base.VisitParameterList(node);
            string indentation = GetIndentation(node.Parent!);
            var visited = (ParameterListSyntax)base.VisitParameterList(node)!;
            var parameters = new List<ParameterSyntax>();
            foreach (ParameterSyntax parameter in visited.Parameters)
                parameters.Add(parameter.WithLeadingTrivia(ParameterLeading(parameter, indentation + "    ")));
            parameters[^1] = parameters[^1].WithTrailingTrivia(TrailingComments(parameters[^1].GetTrailingTrivia()));
            IEnumerable<SyntaxToken> separators = visited.Parameters.GetSeparators().Select(static token => token.WithTrailingTrivia(TrailingComments(token.TrailingTrivia).Add(SyntaxFactory.EndOfLine("\n"))));
            return visited.WithOpenParenToken(visited.OpenParenToken.WithTrailingTrivia(SyntaxFactory.TriviaList(Comments(visited.OpenParenToken.TrailingTrivia)).Add(SyntaxFactory.EndOfLine("\n")))).WithParameters(SyntaxFactory.SeparatedList(parameters, separators)).WithCloseParenToken(visited.CloseParenToken.WithLeadingTrivia(SyntaxFactory.TriviaList(SyntaxFactory.EndOfLine("\n"), SyntaxFactory.Whitespace(indentation))));
        }

        /// <inheritdoc/>
        public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            var result = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node)!;
            if (node.ParameterList.Parameters.Count > 1 && result.Body is not null && result.ConstraintClauses.Count == 0)
                result = result.WithParameterList(ClearTrailing(result.ParameterList)).WithBody(InlineBrace(result.Body));
            return result;
        }

        /// <inheritdoc/>
        public override SyntaxNode? VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            var result = (ConstructorDeclarationSyntax)base.VisitConstructorDeclaration(node)!;
            if (node.ParameterList.Parameters.Count > 1 && result.Body is not null && result.Initializer is null)
                result = result.WithParameterList(ClearTrailing(result.ParameterList)).WithBody(InlineBrace(result.Body));
            return result;
        }

        /// <inheritdoc/>
        public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            var result = (LocalFunctionStatementSyntax)base.VisitLocalFunctionStatement(node)!;
            if (node.ParameterList.Parameters.Count > 1 && result.Body is not null && result.ConstraintClauses.Count == 0)
                result = result.WithParameterList(ClearTrailing(result.ParameterList)).WithBody(InlineBrace(result.Body));
            return result;
        }

        /// <inheritdoc/>
        public override SyntaxNode? VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
        {
            var result = (ParenthesizedLambdaExpressionSyntax)base.VisitParenthesizedLambdaExpression(node)!;
            if (node.ParameterList.Parameters.Count > 1 && result.Body is BlockSyntax block)
                result = result.WithArrowToken(result.ArrowToken.WithTrailingTrivia(Comments(result.ArrowToken.TrailingTrivia))).WithBlock(InlineBrace(block));
            return result;
        }

        /// <inheritdoc/>
        public override SyntaxNode? VisitOperatorDeclaration(OperatorDeclarationSyntax node)
        {
            var result = (OperatorDeclarationSyntax)base.VisitOperatorDeclaration(node)!;
            if (node.ParameterList.Parameters.Count > 1 && result.Body is not null)
                result = result.WithParameterList(ClearTrailing(result.ParameterList)).WithBody(InlineBrace(result.Body));
            return result;
        }

        /// <inheritdoc/>
        public override SyntaxNode? VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node)
        {
            var result = (AnonymousMethodExpressionSyntax)base.VisitAnonymousMethodExpression(node)!;
            if (node.ParameterList?.Parameters.Count > 1)
                result = result.WithParameterList(ClearTrailing(result.ParameterList!)).WithBlock(InlineBrace(result.Block));
            return result;
        }

        private static ParameterListSyntax ClearTrailing(ParameterListSyntax parameters) => parameters.WithCloseParenToken(parameters.CloseParenToken.WithTrailingTrivia(Comments(parameters.CloseParenToken.TrailingTrivia)));
        private static BlockSyntax InlineBrace(BlockSyntax block) => block.WithOpenBraceToken(block.OpenBraceToken.WithLeadingTrivia(SyntaxFactory.TriviaList(SyntaxFactory.Space).AddRange(Comments(block.OpenBraceToken.LeadingTrivia))));
        private static IEnumerable<SyntaxTrivia> Comments(SyntaxTriviaList trivia) => trivia.Where(static value => !value.IsKind(SyntaxKind.WhitespaceTrivia) && !value.IsKind(SyntaxKind.EndOfLineTrivia));
        private static SyntaxTriviaList TrailingComments(SyntaxTriviaList trivia)
        {
            SyntaxTrivia[] comments = Comments(trivia).ToArray();
            return comments.Length == 0 ? default : SyntaxFactory.TriviaList(SyntaxFactory.Space).AddRange(comments);
        }

        private static SyntaxTriviaList ParameterLeading(
            ParameterSyntax parameter,
            string indentation
        ) {
            var leading = SyntaxFactory.TriviaList(SyntaxFactory.Whitespace(indentation));
            foreach (SyntaxTrivia comment in Comments(parameter.GetLeadingTrivia()))
            {
                leading = leading.Add(comment);
                if (comment.IsKind(SyntaxKind.SingleLineCommentTrivia))
                    leading = leading.Add(SyntaxFactory.EndOfLine("\n")).Add(SyntaxFactory.Whitespace(indentation));
            }

            return leading;
        }

        private static string GetIndentation(SyntaxNode node)
        {
            var text = node.SyntaxTree.GetText();
            string line = text.Lines.GetLineFromPosition(node.SpanStart).ToString();
            return new string(line.TakeWhile(static character => character is ' ' or '\t').ToArray());
        }
    }
}
