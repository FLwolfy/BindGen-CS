using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BGCS.Tool.Validation;

internal static class PublicApiDocumentationValidator
{
    private static readonly string[] PlaceholderPrefixes =
    [
        "Defines the public", "Defines values for", "Executes public operation", "Exposes public member",
        "Returns computed data from", "Adds data or behavior through", "Removes data or behavior through",
        "Attempts to resolve data via", "Writes output for", "Performs the operation implemented by",
        "Merges configuration or metadata via", "Persists data using", "Loads data using"
    ];

    internal static int Run(
        string[] arguments,
        TextWriter output,
        TextWriter error
    ) {
        if (arguments.Length != 1 || !Directory.Exists(arguments[0]))
        {
            error.WriteLine("Usage: bindgen-cs validate documentation <source-root>");
            return 2;
        }
        string directory = Path.GetFullPath(arguments[0]);
        SyntaxTree[] trees = Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(static path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(static part => part is "bin" or "obj" or "Generated" or "extern"))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .Select(static path => CSharpSyntaxTree.ParseText(File.ReadAllText(path),
                new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.Diagnose), path))
            .ToArray();
        string[] assemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        CSharpCompilation compilation = CSharpCompilation.Create("DocumentationValidation", trees,
            assemblies.Select(static path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        int failures = 0;
        int declarations = 0;
        foreach (SyntaxTree tree in trees)
        {
            SemanticModel model = compilation.GetSemanticModel(tree);
            foreach (MemberDeclarationSyntax member in tree.GetRoot().DescendantNodes().OfType<MemberDeclarationSyntax>())
            {
                SyntaxNode declaration = member is BaseFieldDeclarationSyntax field
                    ? field.Declaration.Variables[0] : member;
                ISymbol? symbol = model.GetDeclaredSymbol(declaration);
                if (symbol is null || symbol.Kind == SymbolKind.Namespace || !IsAccessible(symbol))
                    continue;
                declarations++;
                DocumentationCommentTriviaSyntax? documentation = member.GetLeadingTrivia()
                    .Select(static trivia => trivia.GetStructure()).OfType<DocumentationCommentTriviaSyntax>().LastOrDefault();
                string location = Path.GetRelativePath(directory, tree.FilePath) + ":"
                    + (member.GetLocation().GetLineSpan().StartLinePosition.Line + 1);
                var messages = new List<string>();
                Validate(symbol, documentation, messages);
                foreach (string message in messages)
                {
                    failures++;
                    error.WriteLine(location + ": " + message);
                }
            }
        }
        output.WriteLine($"Documentation validation: {declarations} accessible declarations, {failures} violations.");
        return failures == 0 ? 0 : 1;
    }

    private static bool IsAccessible(ISymbol symbol)
    {
        for (ISymbol? current = symbol; current is not null && current.Kind != SymbolKind.Namespace; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal))
                return false;
        }
        return true;
    }

    private static void Validate(
        ISymbol symbol,
        DocumentationCommentTriviaSyntax? documentation,
        ICollection<string> messages
    ) {
        if (documentation is null || documentation.ContainsDiagnostics)
        {
            messages.Add("Accessible declarations require valid English XML documentation.");
            return;
        }
        XElement document;
        try
        {
            string xml = string.Join("\n", documentation.ToFullString().Split('\n')
                .Select(static line => line.TrimStart().StartsWith("///", StringComparison.Ordinal)
                    ? line.TrimStart()[3..] : line));
            document = XElement.Parse("<member>" + xml + "</member>");
        }
        catch (System.Xml.XmlException)
        {
            messages.Add("The documentation must contain well-formed XML.");
            return;
        }
        if (document.Element("inheritdoc") is not null)
        {
            if (!HasInheritedMember(symbol))
                messages.Add("Inheritdoc requires an overridden or implemented member.");
            return;
        }
        string summary = document.Element("summary")?.Value.Trim() ?? string.Empty;
        if (summary.Length == 0 || PlaceholderPrefixes.Any(prefix => summary.StartsWith(prefix, StringComparison.Ordinal))
            || string.Equals(summary, "Gets " + symbol.Name + ".", StringComparison.OrdinalIgnoreCase)
            || string.Equals(summary, "Gets or sets " + symbol.Name + ".", StringComparison.OrdinalIgnoreCase)
            || string.Equals(summary, "Gets the default value.", StringComparison.Ordinal))
            messages.Add("The summary must explain behavior or ownership rather than repeat the declaration name.");
        IEnumerable<IParameterSymbol> parameters = symbol switch
        {
            IMethodSymbol method => method.Parameters,
            IPropertySymbol property => property.Parameters,
            INamedTypeSymbol { TypeKind: TypeKind.Delegate } callback => callback.DelegateInvokeMethod!.Parameters,
            _ => []
        };
        foreach (IParameterSymbol parameter in parameters)
            RequireTag(document, "param", parameter.Name, messages);
        if (symbol is INamedTypeSymbol { TypeKind: not TypeKind.Delegate } namedType)
        {
            TypeDeclarationSyntax? declaration = namedType.DeclaringSyntaxReferences
                .Select(static reference => reference.GetSyntax()).OfType<TypeDeclarationSyntax>()
                .FirstOrDefault(static declaration => declaration.ParameterList is not null);
            if (declaration?.ParameterList is not null)
            {
                foreach (ParameterSyntax parameter in declaration.ParameterList.Parameters)
                    RequireTag(document, "param", parameter.Identifier.ValueText, messages);
            }
        }
        IEnumerable<ITypeParameterSymbol> typeParameters = symbol switch
        {
            IMethodSymbol method => method.TypeParameters,
            INamedTypeSymbol type => type.TypeParameters,
            _ => []
        };
        foreach (ITypeParameterSymbol parameter in typeParameters)
            RequireTag(document, "typeparam", parameter.Name, messages);
        ValidateTagNames(document, "param", parameters.Select(static parameter => parameter.Name)
            .Concat(symbol is INamedTypeSymbol primaryType
                ? primaryType.DeclaringSyntaxReferences.Select(static reference => reference.GetSyntax())
                    .OfType<TypeDeclarationSyntax>().SelectMany(static declaration =>
                        declaration.ParameterList?.Parameters.Select(static parameter => parameter.Identifier.ValueText) ?? [])
                : []), messages);
        ValidateTagNames(document, "typeparam", typeParameters.Select(static parameter => parameter.Name), messages);
        bool returnsValue = symbol is IMethodSymbol { ReturnsVoid: false, MethodKind: not MethodKind.Constructor }
            || symbol is INamedTypeSymbol { TypeKind: TypeKind.Delegate, DelegateInvokeMethod.ReturnsVoid: false };
        if (returnsValue && string.IsNullOrWhiteSpace(document.Element("returns")?.Value))
            messages.Add("A value-returning operation requires a returns contract, including absence or failure semantics.");
        else if (returnsValue && (document.Element("returns")!.Value.Trim().StartsWith("The result produced by", StringComparison.Ordinal)
            || document.Element("returns")!.Value.Trim().StartsWith("Result produced by", StringComparison.Ordinal)))
            messages.Add("The returns contract must describe the result rather than repeat the operation name.");
    }

    private static void ValidateTagNames(
        XElement document,
        string tag,
        IEnumerable<string> declaredNames,
        ICollection<string> messages
    ) {
        HashSet<string> names = new(declaredNames, StringComparer.Ordinal);
        foreach (XElement element in document.Elements(tag))
        {
            string? name = (string?)element.Attribute("name");
            if (name is null || !names.Contains(name))
                messages.Add($"The '{name}' {tag} does not identify a declared parameter.");
        }
    }

    private static void RequireTag(
        XElement document,
        string tag,
        string name,
        ICollection<string> messages
    ) {
        XElement[] elements = document.Elements(tag).Where(element => (string?)element.Attribute("name") == name).ToArray();
        if (elements.Length != 1 || string.IsNullOrWhiteSpace(elements[0].Value))
            messages.Add($"The '{name}' {tag} requires an explicit description.");
    }

    private static bool HasInheritedMember(ISymbol symbol)
    {
        if (symbol is IMethodSymbol { OverriddenMethod: not null }
            || symbol is IPropertySymbol { OverriddenProperty: not null }
            || symbol is IEventSymbol { OverriddenEvent: not null })
            return true;
        return symbol.ContainingType is INamedTypeSymbol owner && owner.AllInterfaces.Any(contract =>
            contract.GetMembers().Any(member => SymbolEqualityComparer.Default.Equals(owner.FindImplementationForInterfaceMember(member), symbol)));
    }
}
