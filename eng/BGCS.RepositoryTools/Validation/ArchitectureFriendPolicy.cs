using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BGCS.RepositoryTools.Validation;

internal sealed class ArchitectureFriendPolicy
{
    private sealed record Registration(
        string source,
        string assembly,
        string testProject
    )
    {
        internal int seen;
    }

    private readonly Dictionary<string, Registration> m_registered = new(StringComparer.Ordinal);
    private readonly ICollection<string> m_failures;
    private readonly string m_root;
    private readonly HashSet<string> m_sharedBuildFiles = new(PathComparer);

    private ArchitectureFriendPolicy(
        string root,
        ICollection<string> failures
    ) { m_root = root; m_failures = failures; }

    internal static ArchitectureFriendPolicy Load(
        string root,
        string[] productionProjects,
        ICollection<string> failures
    ) {
        ArchitectureFriendPolicy policy = new(root, failures);
        string path = Path.Combine(root, "eng", "architecture", "test-friends.json");
        if (!File.Exists(path)) return policy; // No registration grants no exceptions.
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.GetProperty("schemaVersion").GetInt32() != 1) throw new InvalidDataException("Unsupported test friend registry revision.");
            HashSet<string> production = productionProjects.Select(Path.GetFullPath).ToHashSet(PathComparer);
            HashSet<string> productionAssemblies = productionProjects.Select(project => AssemblyName(project, XDocument.Load(project))).ToHashSet(StringComparer.Ordinal);
            foreach (JsonElement entry in document.RootElement.GetProperty("exceptions").EnumerateArray())
            {
                string source = policy.Resolve(entry.GetProperty("sourceProject").GetString()!);
                string test = policy.Resolve(entry.GetProperty("friendProject").GetString()!);
                string assembly = entry.GetProperty("friendAssembly").GetString()!;
                string relative = Path.GetRelativePath(root, test).Replace('\\', '/');
                XDocument testDocument = XDocument.Load(test);
                XElement[] packable = testDocument.Descendants().Where(element => element.Name.LocalName == "IsPackable").ToArray();
                if (!production.Contains(source) || production.Contains(test) || !relative.StartsWith("tests/", StringComparison.Ordinal)
                    || productionAssemblies.Contains(assembly) || AssemblyName(test, testDocument) != assembly
                    || packable.Length != 1 || !packable[0].Value.Equals("false", StringComparison.OrdinalIgnoreCase)
                    || packable[0].Attribute("Condition") is not null || packable[0].Ancestors().Any(element => element.Attribute("Condition") is not null)
                    || string.IsNullOrWhiteSpace(entry.GetProperty("reason").GetString()))
                    throw new InvalidDataException("A friend exception must name an existing non-packable test project and its exact assembly, never a production friend.");
                if (!testDocument.Descendants().Where(element => element.Name.LocalName == "ProjectReference").Any(reference =>
                    PathComparer.Equals(Path.GetFullPath(((string?)reference.Attribute("Include") ?? string.Empty).Replace('\\', '/'), Path.GetDirectoryName(test)!), source)))
                    throw new InvalidDataException("A registered friend test must reference its source owner.");
                string key = policy.Key(source, assembly);
                if (!policy.m_registered.TryAdd(key, new(source, assembly, test))) throw new InvalidDataException("A test friend exception is repeated.");
            }
        }
        catch (Exception failure) when (failure is IOException or InvalidDataException or JsonException or InvalidOperationException or ArgumentException
            or KeyNotFoundException or FormatException or System.Xml.XmlException or UnauthorizedAccessException)
        {
            failures.Add("Test friend registry is invalid: " + failure.Message);
        }
        return policy;
    }

    internal void Validate(
        string project,
        XDocument document
    ) {
        string directory = Path.GetDirectoryName(project)!;
        for (string? current = directory; current is not null; current = Path.GetDirectoryName(current))
        {
            foreach (string name in new[] { "Directory.Build.props", "Directory.Build.targets" })
            {
                string shared = Path.Combine(current, name);
                if (!File.Exists(shared) || !m_sharedBuildFiles.Add(shared)) continue;
                if (XDocument.Load(shared).Descendants().Any(element => element.Name.LocalName == "InternalsVisibleTo"
                    || element.Name.LocalName == "AssemblyAttribute" && IsFriend((string?)element.Attribute("Include") ?? string.Empty)))
                    m_failures.Add("Shared build files cannot grant friend assemblies: " + Path.GetRelativePath(m_root, shared));
            }
            if (PathComparer.Equals(current, m_root)) break;
        }
        foreach (string file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories).Where(path => !IsOutput(path)))
        {
            string text = File.ReadAllText(file);
            SyntaxNode original = CSharpSyntaxTree.ParseText(text).GetRoot();
            // Inspect every conditional branch without treating directives inside
            // raw source strings as C# directives. A Debug-only friend is still a friend.
            char[] unconditional = text.ToCharArray();
            foreach (SyntaxTrivia trivia in original.DescendantTrivia(descendIntoTrivia: true))
                if (trivia.GetStructure() is DirectiveTriviaSyntax)
                    for (int index = trivia.Span.Start; index < trivia.Span.End; index++)
                        if (unconditional[index] is not ('\r' or '\n')) unconditional[index] = ' ';
            SyntaxNode syntax = CSharpSyntaxTree.ParseText(new string(unconditional)).GetRoot();
            HashSet<string> aliases = syntax.DescendantNodes().OfType<UsingDirectiveSyntax>()
                .Where(usingDirective => usingDirective.Alias is not null && IsFriend(usingDirective.Name?.ToString() ?? string.Empty))
                .Select(usingDirective => usingDirective.Alias!.Name.Identifier.ValueText).ToHashSet(StringComparer.Ordinal);
            foreach (AttributeSyntax attribute in syntax.DescendantNodes().OfType<AttributeSyntax>())
            {
                string name = attribute.Name.ToString();
                if (!aliases.Contains(name) && !IsFriend(name)) continue;
                AttributeArgumentSyntax[] arguments = attribute.ArgumentList?.Arguments.ToArray() ?? [];
                string? target = arguments.Length == 1 && arguments[0].Expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression)
                    ? literal.Token.ValueText : null;
                Check(project, target, Path.GetRelativePath(m_root, file));
            }
        }
        foreach (XElement item in document.Descendants())
        {
            if (item.Name.LocalName == "InternalsVisibleTo") Check(project, (string?)item.Attribute("Include"), Path.GetRelativePath(m_root, project));
            else if (item.Name.LocalName == "AssemblyAttribute" && IsFriend((string?)item.Attribute("Include") ?? string.Empty))
                Check(project, item.Elements().SingleOrDefault(element => element.Name.LocalName == "_Parameter1")?.Value, Path.GetRelativePath(m_root, project));
        }
    }

    internal void Complete()
    {
        foreach (Registration registration in m_registered.Values)
            if (registration.seen != 1) m_failures.Add("Test friend registration must match one declaration: " + Path.GetRelativePath(m_root, registration.source) + " -> " + registration.assembly);
    }

    private void Check(
        string source,
        string? assembly,
        string location
    ) {
        if (assembly is null || !m_registered.TryGetValue(Key(source, assembly), out Registration? registration))
            m_failures.Add("Unregistered or nonliteral friend assembly in " + location + ": " + (assembly ?? "<dynamic>"));
        else if (++registration.seen != 1) m_failures.Add("Duplicate registered friend assembly in " + location + ": " + assembly);
    }

    private string Resolve(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Replace('\\', '/').Split('/').Any(part => part is "" or "." or ".."))
            throw new InvalidDataException("Friend project paths must be exact repository-relative paths.");
        string path = Path.GetFullPath(relative.Replace('\\', '/'), m_root);
        if (!File.Exists(path) || Path.GetExtension(path) != ".csproj") throw new FileNotFoundException("Registered friend project is unavailable.", path);
        for (string? current = path; current is not null && !PathComparer.Equals(current, m_root); current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Friend project paths cannot traverse symbolic links.");
        return path;
    }

    private string Key(
        string source,
        string assembly
    ) => (OperatingSystem.IsWindows() ? Path.GetFullPath(source).ToUpperInvariant() : Path.GetFullPath(source)) + "\0" + assembly;
    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static bool IsOutput(string path) => path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "bin" or "obj" or "Generated");
    private static bool IsFriend(string name) => name.Replace("global::", string.Empty, StringComparison.Ordinal).Split('.').Last() is "InternalsVisibleTo" or "InternalsVisibleToAttribute";
    private static string AssemblyName(
        string path,
        XDocument document
    ) {
        XElement[] names = document.Descendants().Where(element => element.Name.LocalName == "AssemblyName").ToArray();
        if (names.Length == 0) return Path.GetFileNameWithoutExtension(path);
        if (names.Length != 1 || names[0].Attribute("Condition") is not null || names[0].Ancestors().Any(element => element.Attribute("Condition") is not null)
            || string.IsNullOrWhiteSpace(names[0].Value) || names[0].Value.Contains('$')) throw new InvalidDataException("Friend project assembly identity must be literal and unconditional.");
        return names[0].Value;
    }
}
