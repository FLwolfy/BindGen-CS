using System;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace BGCS.CSharp.Planning;

internal static class CSharpNames
{
    internal static string Identifier(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string value = name.StartsWith('@') ? name[1..] : name;
        bool keyword = SyntaxFacts.GetKeywordKind(value) != SyntaxKind.None;
        if (!keyword && !SyntaxFacts.IsValidIdentifier(value))
            throw new ArgumentException($"'{name}' is not a C# identifier.", nameof(name));
        return keyword || SyntaxFacts.GetContextualKeywordKind(value) != SyntaxKind.None ? "@" + value : value;
    }

    internal static string Namespace(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return string.Join('.', name.Split('.').Select(Identifier));
    }
}
