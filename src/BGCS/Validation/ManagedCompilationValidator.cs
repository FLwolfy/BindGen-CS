using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Infrastructure.Execution;

namespace BGCS.Validation;

/// <summary>Compiles an exact candidate file collection using an isolated .NET 10 SDK project.</summary>
public sealed class ManagedCompilationValidator
{
    /// <summary>Validates generated sources and configured managed dependencies without publishing them.</summary>
    /// <param name="sources">Exact candidate source paths.</param>
    /// <param name="configuration">Effective generation choices.</param>
    /// <param name="baseDirectory">Captured base for additional managed inputs.</param>
    /// <param name="cancellationToken">Cancels SDK execution and waits for process cleanup.</param>
    /// <returns>The actual SDK exit status and diagnostic output.</returns>
    public async Task<ProcessExecutionResult> ValidateAsync(
        IEnumerable<string> sources,
        BindingConfiguration configuration,
        string baseDirectory,
        CancellationToken cancellationToken
    ) {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(configuration);
        string[] generated = sources.ToArray();
        if (generated.Length == 0) throw new InvalidOperationException("The candidate contains no managed source files to validate.");
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-managed-validation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            XElement properties = new("PropertyGroup", new XElement("TargetFramework", "net10.0"), new XElement("LangVersion", "14.0"),
                new XElement("AllowUnsafeBlocks", "true"), new XElement("Nullable", "enable"), new XElement("TreatWarningsAsErrors", "true"),
                new XElement("EnableDefaultCompileItems", "false"), new XElement("BGCSGenerateBindings", "false"), new XElement("EnableNETAnalyzers", "false"));
            if (!configuration.managedBuild.defines.IsDefaultOrEmpty)
            {
                foreach (string symbol in configuration.managedBuild.defines)
                    if (symbol.Any(character => !char.IsLetterOrDigit(character) && character != '_') || symbol.Length == 0 || char.IsDigit(symbol[0]))
                        throw new ArgumentException($"Managed compilation symbol '{symbol}' is invalid.");
                properties.Add(new XElement("DefineConstants", "$(DefineConstants);" + string.Join(';', configuration.managedBuild.defines)));
            }
            XElement items = new("ItemGroup");
            foreach (string source in generated.Concat(configuration.managedBuild.sources.Select(path => Path.GetFullPath(path, baseDirectory))).Distinct(StringComparer.Ordinal))
            {
                string path = Path.GetFullPath(source);
                if (!File.Exists(path)) throw new FileNotFoundException("A managed compilation source is missing.", path);
                items.Add(new XElement("Compile", new XAttribute("Include", EscapeItem(path))));
            }
            IEnumerable<string> references = configuration.managedBuild.references.Select(path => Path.GetFullPath(path, baseDirectory));
            if (configuration.runtime.mode == "package") references = references.Append(typeof(BGCS.Runtime.NativeModuleSession).Assembly.Location);
            int referenceIndex = 0;
            foreach (string reference in references.Distinct(StringComparer.Ordinal))
            {
                if (!File.Exists(reference)) throw new FileNotFoundException("A managed compilation reference is missing.", reference);
                items.Add(new XElement("Reference", new XAttribute("Include", "ExplicitReference" + referenceIndex++), new XElement("HintPath", EscapeItem(reference))));
            }
            string project = Path.Combine(temporary, "Candidate.csproj");
            new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"), properties, items)).Save(project);
            ManagedSdkSelection sdk = ManagedSdkEnvironment.Resolve();
            await File.WriteAllTextAsync(Path.Combine(temporary, "global.json"),
                JsonSerializer.Serialize(new { sdk = new { version = sdk.sdkVersion, rollForward = "latestPatch", allowPrerelease = false } }), cancellationToken).ConfigureAwait(false);
            return await ProcessExecutor.ExecuteAsync(sdk.executable, ["build", project, "-v:minimal", "-m:1", "-nr:false", "-p:UseSharedCompilation=false"],
                temporary, TimeSpan.FromSeconds(configuration.execution.managedCompilationTimeoutSeconds), cancellationToken,
                environmentOverrides: sdk.environment).ConfigureAwait(false);
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    private static string EscapeItem(string path) => path.Replace("%", "%25", StringComparison.Ordinal).Replace("$", "%24", StringComparison.Ordinal)
        .Replace("@", "%40", StringComparison.Ordinal).Replace(";", "%3B", StringComparison.Ordinal).Replace("'", "%27", StringComparison.Ordinal)
        .Replace("(", "%28", StringComparison.Ordinal).Replace(")", "%29", StringComparison.Ordinal).Replace("*", "%2A", StringComparison.Ordinal).Replace("?", "%3F", StringComparison.Ordinal);
}
