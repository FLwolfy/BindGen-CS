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

internal static class ArchitectureValidator
{
    internal static int Run(
        string[] arguments,
        TextWriter output,
        TextWriter error
    ) {
        if (arguments.Length != 1)
        {
            error.WriteLine("Usage: dotnet BGCS.RepositoryTools.dll validate architecture <repository-root>");
            return 2;
        }
        string root = Path.GetFullPath(arguments[0]);
        string sourceRoot = Path.Combine(root, "src");
        if (!Directory.Exists(sourceRoot))
        {
            error.WriteLine($"Production source root is unavailable: {sourceRoot}");
            return 2;
        }
        string extensionRoot = Path.Combine(root, "extensions");
        Dictionary<string, string[]> allowedDependencies = new(StringComparer.Ordinal);
        HashSet<string> executables;
        try
        {
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "eng", "architecture", "project-dependencies.json")));
            if (manifest.RootElement.GetProperty("schemaVersion").GetInt32() != 1)
                throw new InvalidDataException("The architecture manifest revision is unsupported.");
            foreach (JsonProperty project in manifest.RootElement.GetProperty("projects").EnumerateObject())
                allowedDependencies.Add(project.Name, project.Value.EnumerateArray().Select(value => value.GetString()!).ToArray());
            executables = manifest.RootElement.GetProperty("executables").EnumerateArray().Select(value => value.GetString()!).ToHashSet(StringComparer.Ordinal);
            if (allowedDependencies.Count == 0 || executables.Any(name => !allowedDependencies.ContainsKey(name))
                || allowedDependencies.Values.SelectMany(dependencies => dependencies).Any(name => !allowedDependencies.ContainsKey(name)))
                throw new InvalidDataException("The architecture manifest must describe every production owner and its permitted dependency direction.");
        }
        catch (Exception failure) when (failure is IOException or JsonException or InvalidOperationException)
        {
            error.WriteLine($"Architecture manifest could not be read: {failure.Message}");
            return 2;
        }
        string[] projects = Directory.GetFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories)
            .Concat(Directory.Exists(extensionRoot) ? Directory.GetFiles(extensionRoot, "*.csproj", SearchOption.AllDirectories) : [])
            .Where(static path => !IsOutput(path)).OrderBy(static path => path, StringComparer.Ordinal).ToArray();
        var failures = new List<string>();
        ArchitectureVisibilityPolicy visibility = new(root, failures);
        var discovered = new HashSet<string>(StringComparer.Ordinal);
        foreach (string project in projects)
        {
            string name = Path.GetFileNameWithoutExtension(project);
            if (!discovered.Add(name) || !allowedDependencies.TryGetValue(name, out string[]? allowed))
            {
                failures.Add($"Unexpected or duplicate production project: {Path.GetRelativePath(root, project)}");
                continue;
            }
            XDocument document = XDocument.Load(project);
            string[] actualDependencies = document.Descendants("ProjectReference")
                .Select(reference => Path.GetFileNameWithoutExtension(((string?)reference.Attribute("Include") ?? string.Empty).Replace('\\', '/')))
                .OrderBy(value => value, StringComparer.Ordinal).ToArray();
            if (!actualDependencies.SequenceEqual(allowed.OrderBy(value => value, StringComparer.Ordinal), StringComparer.Ordinal))
                failures.Add($"{name}: declared project dependencies differ from the reviewed architecture matrix.");
            foreach (XElement reference in document.Descendants("ProjectReference"))
            {
                string path = ((string?)reference.Attribute("Include") ?? string.Empty).Replace('\\', '/');
                string dependency = Path.GetFileNameWithoutExtension(path);
                string resolved = Path.GetFullPath(path, Path.GetDirectoryName(project)!);
                if (!File.Exists(resolved) || !allowed.Contains(dependency, StringComparer.Ordinal)
                    || !projects.Contains(resolved, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
                    failures.Add($"{name}: forbidden or missing dependency '{path}'.");
            }
            bool executable = document.Descendants("OutputType").Any(static element => element.Value is "Exe" or "WinExe");
            if (executable != executables.Contains(name))
                failures.Add($"{name}: executable status does not match the architecture manifest.");
            foreach (XElement item in document.Descendants("Compile"))
            {
                if (item.Attribute("Link") is not null || item.Element("Link") is not null)
                    failures.Add($"{name}: linked production sources violate source ownership.");
                string? include = (string?)item.Attribute("Include");
                if (include is not null && include.Replace('\\', '/').Contains("../", StringComparison.Ordinal))
                    failures.Add($"{name}: production source is included from another owner.");
            }
            if (name is "BGCS.Infrastructure" or "BGCS.Core" or "BGCS.Runtime")
                ValidateNeutralSources(Path.GetDirectoryName(project)!, name, failures);
            else if (name != "BGCS.Frontend.Clang")
                ValidateParserOwnership(Path.GetDirectoryName(project)!, name, failures);
            visibility.Validate(project, document);
            if (name is "BGCS.Core" or "BGCS.Infrastructure" or "BGCS.Analysis" or "BGCS.NativeBridge" or "BGCS.CSharp" or "BGCS.Engine" or "BGCS.Runtime")
                ValidatePlatformNeutrality(Path.GetDirectoryName(project)!, name, failures);
        }
        foreach (string required in allowedDependencies.Keys.Where(name => !discovered.Contains(name)))
            failures.Add($"Missing production project: {required}");
        foreach (string failure in failures)
            error.WriteLine(failure);
        output.WriteLine($"Architecture validation: {projects.Length} production projects, {failures.Count} violations.");
        return failures.Count == 0 ? 0 : 1;
    }

    private static void ValidatePlatformNeutrality(string directory, string owner, ICollection<string> failures)
    {
        foreach (string file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories).Where(path => !IsOutput(path)))
        {
            SyntaxNode syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
            foreach (MemberAccessExpressionSyntax member in syntax.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
                if (member.Expression.ToString() is "OperatingSystem" or "System.OperatingSystem" or "RuntimeInformation" or "System.Runtime.InteropServices.RuntimeInformation")
                    failures.Add($"{owner}: platform selection belongs to an explicit implementation: {Path.GetFileName(file)}:{member.SpanStart}.");
        }
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
                if (value.StartsWith("BGCS.Frontend.Clang", StringComparison.Ordinal)
                    || value.StartsWith("ClangSharp", StringComparison.Ordinal)
                    || project != "BGCS.Infrastructure" && value.StartsWith("BGCS.", StringComparison.Ordinal) && value != project && !value.StartsWith(project + ".", StringComparison.Ordinal)
                    || value.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal)
                    || value.StartsWith("Microsoft.Build", StringComparison.Ordinal))
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
                .Any(name => name.ToString().Replace("global::", string.Empty, StringComparison.Ordinal)
                    .StartsWith("ClangSharp", StringComparison.Ordinal)
                    || project is not ("BGCS.Frontend.Clang.Worker" or "BGCS" or "BGCS.Tool") && name.ToString().Replace("global::", string.Empty, StringComparison.Ordinal)
                        .StartsWith("BGCS.Frontend.Clang", StringComparison.Ordinal));
            if (usesNativeParser)
                failures.Add($"{project}: native parser interop must remain inside BGCS.Frontend.Clang: {Path.GetRelativePath(directory, file)}");
        }
    }
}
