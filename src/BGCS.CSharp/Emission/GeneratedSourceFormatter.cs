using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace BGCS.CSharp.Emission;

internal static class GeneratedSourceFormatter
{
    internal static string Normalize(
        SyntaxNode root,
        string endOfLine
    ) {
        SyntaxTrivia[] original = Documentation(root);
        SyntaxNode formatted = root.NormalizeWhitespace("    ", endOfLine);
        SyntaxTrivia[] normalized = Documentation(formatted);
        if (original.Length != normalized.Length)
            throw new InvalidOperationException("Source formatting changed the documentation comment set.");
        Dictionary<int, SyntaxTrivia> originalsByPosition = normalized.Select((
            trivia,
            index
        ) => (trivia.SpanStart, Original: original[index]))
            .ToDictionary(item => item.SpanStart, item => item.Original);
        // Roslyn's XML formatter can separate adjacent entities, e.g. '&amp;&amp;'
        // becomes '&amp; &amp;'. Preserve every original prose byte apart from the
        // requested line endings and indentation of exterior '///' delimiters.
        return formatted.ReplaceTrivia(normalized, (
            trivia,
            _
        ) => Restore(originalsByPosition[trivia.SpanStart], trivia, endOfLine)).ToFullString();
    }

    private static SyntaxTrivia[] Documentation(SyntaxNode root) => root.DescendantTrivia(descendIntoTrivia: false)
        .Where(trivia => trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)).ToArray();

    private static SyntaxTrivia Restore(
        SyntaxTrivia original,
        SyntaxTrivia normalized,
        string endOfLine
    ) {
        string indent = string.Empty;
        foreach (SyntaxTrivia leading in normalized.Token.LeadingTrivia)
        {
            if (leading == normalized) break;
            if (leading.IsKind(SyntaxKind.WhitespaceTrivia)) indent = leading.ToFullString();
            else if (leading.IsKind(SyntaxKind.EndOfLineTrivia)) indent = string.Empty;
        }
        string[] lines = original.ToFullString().Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        for (int index = 1; index < lines.Length; index++)
        {
            string content = lines[index].TrimStart(' ', '\t');
            if (content.StartsWith("///", StringComparison.Ordinal)) lines[index] = indent + content;
        }
        return SyntaxFactory.ParseLeadingTrivia(string.Join(endOfLine, lines))
            .Single(trivia => trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia));
    }
}
