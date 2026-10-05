using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BGCS.Intermediate.Emission;

namespace BGCS.Emission;

using System.Reflection;
using System.Text;
using BGCS.Intermediate;

/// <summary>
/// Emits the optional standalone BGCS Runtime source from embedded package resources.
/// </summary>
public sealed class RuntimeEmitter : IBindingEmitter
{
    private const string C_RESOURCEPREFIX = "BGCS.RuntimeSources.";
    private const string C_EXTERNALRUNTIMESYMBOL = "BGCS_RUNTIME_EXTERNAL";
    /// <inheritdoc/>
    public string name => "Standalone Runtime emitter";

    /// <inheritdoc/>
    public IReadOnlyList<string> Emit(
        BindingModule module,
        EmissionContext context
    ) {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(context);
        Assembly assembly = typeof(RuntimeEmitter).Assembly;
        List<(string Path, string Text)> sources = [];
        foreach (string resourceName in assembly.GetManifestResourceNames().Where(name => name.StartsWith(global::BGCS.Emission.RuntimeEmitter.C_RESOURCEPREFIX, StringComparison.Ordinal)).OrderBy(name => name, StringComparer.Ordinal))
        {
            using Stream stream = assembly.GetManifestResourceStream(resourceName) ?? throw new InvalidOperationException($"Embedded Runtime resource '{resourceName}' could not be opened.");
            using StreamReader reader = new(stream, Encoding.UTF8, true);
            string text = RewriteNamespace(reader.ReadToEnd(), context.runtimeNamespace);
            sources.Add((resourceName, text));
        }

        if (sources.Count == 0)
            throw new InvalidOperationException("No embedded BGCS Runtime sources were found.");
        string outputFile = Path.Combine(context.outputPath, "Runtime.cs");
        new SingleFileComposer().ComposeSources(sources, outputFile, context.runtimeNamespace);
        string content = File.ReadAllText(outputFile);
        File.WriteAllText(outputFile, $"#if !{C_EXTERNALRUNTIMESYMBOL}{Environment.NewLine}#nullable enable{Environment.NewLine}{content}#endif{Environment.NewLine}");
        return [outputFile];
    }

    private static string RewriteNamespace(
        string source,
        string targetNamespace
    ) {
        if (targetNamespace == "BGCS.Runtime")
            return source;
        return source.Replace("namespace BGCS.Runtime;", $"namespace {targetNamespace};", StringComparison.Ordinal).Replace("namespace BGCS.Runtime", $"namespace {targetNamespace}", StringComparison.Ordinal);
    }
}
