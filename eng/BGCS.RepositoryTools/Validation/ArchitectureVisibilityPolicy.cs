using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis;

namespace BGCS.RepositoryTools.Validation;

internal sealed class ArchitectureVisibilityPolicy(
    string root,
    ICollection<string> failures
)
{
    private readonly HashSet<string> m_buildFiles = new(StringComparer.Ordinal);

    internal void Validate(
        string project,
        XDocument document
    ) {
        CheckItems(project, document);
        string directory = Path.GetDirectoryName(project)!;
        for (string? parent = directory; parent is not null; parent = Path.GetDirectoryName(parent))
        {
            foreach (string name in new[] { "Directory.Build.props", "Directory.Build.targets" })
            {
                string path = Path.Combine(parent, name);
                if (File.Exists(path) && m_buildFiles.Add(path)) CheckItems(path, XDocument.Load(path));
            }
            if (Path.GetFullPath(parent) == Path.GetFullPath(root)) break;
        }
        foreach (string file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "bin" or "obj")))
        {
            SyntaxNode syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
            if (HasFriend(syntax))
                failures.Add("Production assemblies cannot grant friend access, including to tests: " + Path.GetRelativePath(root, file));
        }
    }

    private static bool HasFriend(
        SyntaxNode syntax,
        IEnumerable<string>? inheritedAliases = null
    ) {
        HashSet<string> aliases = syntax.DescendantNodes().OfType<UsingDirectiveSyntax>()
            .Where(item => item.Alias is not null && item.Name is not null && IsFriend(item.Name.ToString()))
            .Select(item => item.Alias!.Name.Identifier.ValueText).ToHashSet(StringComparer.Ordinal);
        aliases.UnionWith(inheritedAliases ?? []);
        if (syntax.DescendantNodes().OfType<AttributeSyntax>().Any(attribute => IsFriend(attribute.Name.ToString()) || aliases.Contains(attribute.Name.ToString())))
            return true;
        return syntax.DescendantTrivia(descendIntoTrivia: true).Where(trivia => trivia.IsKind(SyntaxKind.DisabledTextTrivia))
            .Any(trivia => HasFriend(CSharpSyntaxTree.ParseText(trivia.ToFullString()).GetRoot(), aliases));
    }

    private void CheckItems(
        string path,
        XDocument document
    ) {
        if (document.Descendants().Any(item => item.Name.LocalName == "InternalsVisibleTo"
            || item.Name.LocalName == "AssemblyAttribute" && IsFriend((string?)item.Attribute("Include") ?? string.Empty)))
            failures.Add("Build files cannot grant friend assemblies: " + Path.GetRelativePath(root, path));
    }

    private static bool IsFriend(string name) => name.Replace("global::", string.Empty, StringComparison.Ordinal)
        .Split('.').Last() is "InternalsVisibleTo" or "InternalsVisibleToAttribute";
}
