using System;
using System.Collections.Generic;
using System.IO;
using BGCS.Core.Writing;
using BGCS.Intermediate.Bridges;
using BGCS.Intermediate.Emission;

namespace BGCS.Cpp2C.Emission;

/// <summary>
/// Writes native bridge artifacts exclusively from frozen, fully lowered source operations.
/// </summary>
public sealed class CBridgeEmitter : ICppBridgeEmitter
{
    /// <inheritdoc/>
    /// <exception cref = "ArgumentNullException">
    /// The module or emission context is null.
    /// </exception>
    /// <exception cref = "InvalidOperationException">
    /// Artifact paths escape the output root, repeat, or contain invalid source operations.
    /// </exception>
    public IReadOnlyList<string> Emit(
        CppBridgeModule module,
        EmissionContext context
    ) {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(context);
        string outputRoot = Path.GetFullPath(context.outputPath);
        StringComparer comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        HashSet<string> paths = new(comparer);
        List<(string path, CppBridgeArtifact artifact)> outputs = [];
        foreach (CppBridgeArtifact artifact in module.artifacts)
        {
            string path = Path.GetFullPath(artifact.relativePath, outputRoot);
            string relative = Path.GetRelativePath(outputRoot, path);
            if (Path.IsPathRooted(artifact.relativePath) || Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidOperationException($"Bridge artifact '{artifact.relativePath}' escapes its output root.");
            if (!paths.Add(path))
                throw new InvalidOperationException($"Bridge artifacts repeat output path '{artifact.relativePath}'.");
            ValidateOperations(artifact);
            outputs.Add((path, artifact));
        }

        foreach ((string path, CppBridgeArtifact artifact) in outputs)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using CodeWriter writer = new(path);
            foreach (CppBridgeOperation operation in artifact.operations)
                Apply(writer, operation);
        }

        return outputs.ConvertAll(static output => output.path).AsReadOnly();
    }

    private static void ValidateOperations(CppBridgeArtifact artifact)
    {
        int blocks = 0;
        int indentation = 0;
        foreach (CppBridgeOperation operation in artifact.operations)
        {
            switch (operation.kind)
            {
                case CppBridgeOperationKind.BeginBlock:
                    blocks++;
                    indentation++;
                    break;
                case CppBridgeOperationKind.EndBlock:
                    if (blocks == 0)
                        throw new InvalidOperationException($"Bridge artifact '{artifact.relativePath}' closes an unopened block.");
                    blocks--;
                    indentation--;
                    break;
                case CppBridgeOperationKind.Indent:
                case CppBridgeOperationKind.Unindent:
                    if (operation.count < 0)
                        throw new InvalidOperationException("Bridge indentation counts cannot be negative.");
                    indentation += operation.kind == CppBridgeOperationKind.Indent ? operation.count : -operation.count;
                    break;
                case CppBridgeOperationKind.Fragment:
                case CppBridgeOperationKind.Line:
                    break;
                default:
                    throw new InvalidOperationException($"Unknown bridge operation '{operation.kind}'.");
            }

            if (indentation < 0)
                throw new InvalidOperationException($"Bridge artifact '{artifact.relativePath}' has negative indentation.");
        }

        if (blocks != 0 || indentation != 0)
            throw new InvalidOperationException($"Bridge artifact '{artifact.relativePath}' has unbalanced source scopes.");
    }

    private static void Apply(
        CodeWriter writer,
        CppBridgeOperation operation
    ) {
        switch (operation.kind)
        {
            case CppBridgeOperationKind.Fragment:
                writer.Write(operation.syntax);
                break;
            case CppBridgeOperationKind.Line:
                writer.WriteLine(operation.syntax);
                break;
            case CppBridgeOperationKind.BeginBlock:
                writer.BeginBlock(operation.syntax);
                break;
            case CppBridgeOperationKind.EndBlock:
                writer.EndBlock(operation.syntax);
                break;
            case CppBridgeOperationKind.Indent:
                writer.Indent(operation.count);
                break;
            case CppBridgeOperationKind.Unindent:
                writer.Unindent(operation.count);
                break;
        }
    }
}
