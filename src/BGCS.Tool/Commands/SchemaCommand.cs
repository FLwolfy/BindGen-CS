using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using BGCS.Configuration;

namespace BGCS.Tool.Commands;

internal static class SchemaCommand
{
    internal static int Run(
        string[] args,
        string workingDirectory,
        TextWriter output,
        TextWriter error
    ) {
        try
        {
            string kind = "binding";
            string? destination = null;
            for (int index = 0; index < args.Length; index++)
            {
                if (args[index] == "--kind")
                {
                    if (++index >= args.Length) throw new ArgumentException("--kind requires binding, workspace or plugin.");
                    kind = args[index];
                }
                else if (!args[index].StartsWith('-') && destination is null) destination = args[index];
                else throw new ArgumentException($"Unknown schema argument '{args[index]}'.");
            }
            SchemaCatalog catalog = new();
            JsonObject schema = kind switch
            {
                "binding" => catalog.CreateBindingSchema(),
                "plugin" => catalog.CreatePluginSchema(),
                "workspace" => catalog.CreateWorkspaceSchema(),
                _ => throw new ArgumentException("--kind must be binding, workspace or plugin.")
            };
            string path = Path.GetFullPath(destination ?? (kind == "binding" ? "bindgen.schema.json" : kind + ".schema.json"), workingDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, schema.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
            output.WriteLine($"Created {path}");
            return 0;
        }
        catch (ArgumentException exception)
        {
            error.WriteLine($"error: {exception.Message}");
            return 2;
        }
    }
}
