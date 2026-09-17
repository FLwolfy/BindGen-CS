namespace BGCS.Emission;

using System.Reflection;
using System.Text;
using BGCS.Intermediate;

/// <summary>
/// Emits the optional standalone BGCS Runtime source from embedded package resources.
/// </summary>
public sealed class RuntimeEmitter : IBindingEmitter
{
    private const string ResourcePrefix = "BGCS.RuntimeSources.";
    private const string ExternalRuntimeSymbol = "BGCS_RUNTIME_EXTERNAL";

    public string Name => "Standalone Runtime emitter";

    public IReadOnlyList<string> Emit(BindingModule module, EmissionContext context)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(context);
        Assembly assembly = typeof(RuntimeEmitter).Assembly;
        List<(string Path, string Text)> sources = [];
        foreach (string resourceName in assembly.GetManifestResourceNames()
                     .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                     .OrderBy(name => name, StringComparer.Ordinal))
        {
            using Stream stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded Runtime resource '{resourceName}' could not be opened.");
            using StreamReader reader = new(stream, Encoding.UTF8, true);
            string text = RewriteNamespace(reader.ReadToEnd(), context.RuntimeNamespace);
            sources.Add((resourceName, text));
        }
        if (sources.Count == 0)
            throw new InvalidOperationException("No embedded BGCS Runtime sources were found.");

        string outputFile = Path.Combine(context.OutputPath, "Runtime.cs");
        new SingleFileComposer().ComposeSources(sources, outputFile, context.RuntimeNamespace);
        string content = File.ReadAllText(outputFile);
        File.WriteAllText(outputFile,
            $"#if !{ExternalRuntimeSymbol}{Environment.NewLine}#nullable enable{Environment.NewLine}{content}#endif{Environment.NewLine}");
        return [outputFile];
    }

    private static string RewriteNamespace(string source, string targetNamespace)
    {
        if (targetNamespace == "BGCS.Runtime")
            return source;
        return source.Replace("namespace BGCS.Runtime;", $"namespace {targetNamespace};", StringComparison.Ordinal)
            .Replace("namespace BGCS.Runtime", $"namespace {targetNamespace}", StringComparison.Ordinal);
    }
}
