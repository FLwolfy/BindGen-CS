using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynSyntaxNode = Microsoft.CodeAnalysis.SyntaxNode;
using RoslynSyntaxTree = Microsoft.CodeAnalysis.SyntaxTree;

namespace BGCS.Language.CSharp.Preprocessing;

/// <summary>
/// Evaluates C# conditional directives and removes excluded source without changing source offsets or line endings.
/// </summary>
/// <remarks>
/// This is an authoring-language operation. Native C/C++ preprocessing belongs to the Clang frontend.
/// Each invocation has its own directive state; source-defined symbols never affect later invocations.
/// </remarks>
public sealed class CSharpPreprocessor
{
    private readonly CSharpParseOptions m_options;

    /// <summary>
    /// Captures the symbols defined before a source file's own directives execute.
    /// </summary>
    /// <param name="symbols">The initial symbol names, or null for no initial symbols; duplicates are ignored.</param>
    /// <exception cref="ArgumentException">A symbol is not a valid unescaped C# identifier.</exception>
    public CSharpPreprocessor(IEnumerable<string>? symbols = null)
    {
        string[] names = symbols?.Distinct(StringComparer.Ordinal).ToArray() ?? [];
        foreach (string name in names)
            if (string.IsNullOrEmpty(name) || name[0] == '@' || !SyntaxFacts.IsValidIdentifier(name))
                throw new ArgumentException("A preprocessing symbol must be an unescaped C# identifier.", nameof(symbols));
        m_options = new CSharpParseOptions(LanguageVersion.CSharp13, preprocessorSymbols: names);
    }

    /// <summary>
    /// Replaces directive and inactive spans with spaces while retaining every CR/LF and active source character.
    /// </summary>
    /// <param name="text">The complete C# source text; an empty file remains empty.</param>
    /// <param name="filename">The source name used in directive error locations.</param>
    /// <returns>An equally long string containing the active source, with unchanged UTF-16 offsets.</returns>
    /// <exception cref="ArgumentNullException">The source text or source name is null.</exception>
    /// <exception cref="FormatException">Directives are invalid, unbalanced, or contain an active #error.</exception>
    public string Process(
        string text,
        string filename
    ) {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(filename);
        RoslynSyntaxTree tree = CSharpSyntaxTree.ParseText(text, m_options, filename);
        RoslynSyntaxNode root = tree.GetRoot();
        SyntaxTrivia[] removed = root.DescendantTrivia(descendIntoTrivia: false)
            .Where(static trivia => trivia.IsKind(SyntaxKind.DisabledTextTrivia)
                || trivia.GetStructure() is DirectiveTriviaSyntax).ToArray();
        foreach (Diagnostic diagnostic in tree.GetDiagnostics())
        {
            if (diagnostic.Severity == DiagnosticSeverity.Error
                && (IsDirectiveError(diagnostic.Id)
                    || removed.Any(trivia => trivia.GetStructure() is DirectiveTriviaSyntax
                        && trivia.FullSpan.IntersectsWith(diagnostic.Location.SourceSpan))))
                throw new FormatException(diagnostic.ToString());
        }
        if (removed.Length == 0)
            return text;
        char[] result = text.ToCharArray();
        foreach (SyntaxTrivia trivia in removed)
            for (int index = trivia.FullSpan.Start; index < trivia.FullSpan.End; index++)
                if (result[index] is not ('\r' or '\n'))
                    result[index] = ' ';
        return new string(result);
    }

    private static bool IsDirectiveError(string id) => id is
        "CS1024" or "CS1025" or "CS1027" or "CS1028" or "CS1029" or "CS1032" or "CS1038" or "CS1040" or "CS1517";
}
