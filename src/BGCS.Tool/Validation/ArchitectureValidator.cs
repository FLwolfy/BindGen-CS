using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BGCS.Tool.Validation;

internal static class ArchitectureValidator
{
    private static readonly IReadOnlyDictionary<string, string[]> AllowedDependencies = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["BGCS.Core"] = [],
        ["BGCS.CppAst"] = ["BGCS.Core"],
        ["BGCS.Intermediate"] = [],
        ["BGCS"] = ["BGCS.Core", "BGCS.CppAst", "BGCS.Intermediate", "BGCS.Language"],
        ["BGCS.Cpp2C"] = ["BGCS.Core", "BGCS.CppAst", "BGCS.Intermediate"],
        ["BGCS.Language"] = ["BGCS.Core"],
        ["BGCS.Runtime"] = [],
        ["BGCS.Tool"] = ["BGCS", "BGCS.Cpp2C"]
    };

    internal static int Run(
        string[] arguments,
        TextWriter output,
        TextWriter error
    ) {
        if (arguments.Length != 1)
        {
            error.WriteLine("Usage: bindgen-cs validate architecture <repository-root>");
            return 2;
        }
        string root = Path.GetFullPath(arguments[0]);
        string sourceRoot = Path.Combine(root, "src");
        if (!Directory.Exists(sourceRoot))
        {
            error.WriteLine($"Production source root is unavailable: {sourceRoot}");
            return 2;
        }
        string[] projects = Directory.GetFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(static path => !IsOutput(path)).OrderBy(static path => path, StringComparer.Ordinal).ToArray();
        var failures = new List<string>();
        var discovered = new HashSet<string>(StringComparer.Ordinal);
        foreach (string project in projects)
        {
            string name = Path.GetFileNameWithoutExtension(project);
            if (!discovered.Add(name) || !AllowedDependencies.TryGetValue(name, out string[]? allowed))
            {
                failures.Add($"Unexpected or duplicate production project: {Path.GetRelativePath(root, project)}");
                continue;
            }
            XDocument document = XDocument.Load(project);
            foreach (XElement reference in document.Descendants("ProjectReference"))
            {
                string path = ((string?)reference.Attribute("Include") ?? string.Empty).Replace('\\', '/');
                string dependency = Path.GetFileNameWithoutExtension(path);
                string resolved = Path.GetFullPath(path, Path.GetDirectoryName(project)!);
                if (!File.Exists(resolved) || !allowed.Contains(dependency, StringComparer.Ordinal)
                    || !resolved.StartsWith(sourceRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    failures.Add($"{name}: forbidden or missing dependency '{path}'.");
            }
            bool executable = document.Descendants("OutputType").Any(static element => element.Value is "Exe" or "WinExe");
            if (executable != (name == "BGCS.Tool"))
                failures.Add($"{name}: BGCS.Tool must be the sole production executable.");
            foreach (XElement item in document.Descendants("Compile"))
            {
                if (item.Attribute("Link") is not null || item.Element("Link") is not null)
                    failures.Add($"{name}: linked production sources violate source ownership.");
                string? include = (string?)item.Attribute("Include");
                if (include is not null && include.Replace('\\', '/').Contains("../", StringComparison.Ordinal))
                    failures.Add($"{name}: production source is included from another owner.");
            }
            if (name is "BGCS.Core" or "BGCS.Intermediate" or "BGCS.Runtime")
                ValidateNeutralSources(Path.GetDirectoryName(project)!, name, failures);
            else if (name != "BGCS.CppAst")
                ValidateParserOwnership(Path.GetDirectoryName(project)!, name, failures);
        }
        foreach (string required in AllowedDependencies.Keys.Where(name => !discovered.Contains(name)))
            failures.Add($"Missing production project: {required}");
        foreach (string failure in failures)
            error.WriteLine(failure);
        output.WriteLine($"Architecture validation: {projects.Length} production projects, {failures.Count} violations.");
        return failures.Count == 0 ? 0 : 1;
    }

    private static void ValidateNeutralSources(
        string directory,
        string project,
        ICollection<string> failures
    ) {
        foreach (string file in Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories).Where(static path => !IsOutput(path)))
        {
            var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(file), new CSharpParseOptions(LanguageVersion.Preview)).GetRoot();
            foreach (QualifiedNameSyntax name in syntax.DescendantNodes().OfType<QualifiedNameSyntax>())
            {
                string value = name.ToString().Replace("global::", string.Empty, StringComparison.Ordinal);
                if (value.StartsWith("BGCS.CppAst", StringComparison.Ordinal)
                    || value.StartsWith("ClangSharp", StringComparison.Ordinal)
                    || project != "BGCS.Core" && value.StartsWith("BGCS.Core", StringComparison.Ordinal))
                {
                    failures.Add($"{project}: implementation dependency in {Path.GetRelativePath(directory, file)}: {value}");
                    break;
                }
            }
        }
    }

    private static bool IsOutput(string path)
        => path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(static part => part is "bin" or "obj" or "Generated");

    private static void ValidateParserOwnership(
        string directory,
        string project,
        ICollection<string> failures
    ) {
        foreach (string file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(static path => !IsOutput(path)))
        {
            SyntaxNode syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
            bool usesNativeParser = syntax.DescendantNodes().OfType<QualifiedNameSyntax>()
                .Any(static name => name.ToString().Replace("global::", string.Empty, StringComparison.Ordinal)
                    .StartsWith("ClangSharp", StringComparison.Ordinal));
            if (usesNativeParser)
                failures.Add($"{project}: native parser interop must remain inside BGCS.CppAst: {Path.GetRelativePath(directory, file)}");
        }
    }
}
