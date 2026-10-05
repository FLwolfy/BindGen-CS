using System;
using System.Collections.Generic;
using System.Linq;

namespace BGCS.Intermediate.Bridges;

/// <summary>
/// Freezes the source operations for one relative bridge output file.
/// </summary>
public sealed class CppBridgeArtifact
{
    /// <summary>
    /// Copies source operations into an immutable output artifact.
    /// </summary>
    /// <param name = "relativePath">
    /// A path beneath the caller's native output root.
    /// </param>
    /// <param name = "operations">
    /// The fully lowered operations in source order.
    /// </param>
    /// <param name = "exposeToBindings">
    /// Whether the native header must be included by the analysis-owned binding umbrella.
    /// </param>
    /// <exception cref = "ArgumentException">
    /// The relative path is empty.
    /// </exception>
    /// <exception cref = "ArgumentNullException">
    /// The operations or one of their elements is null.
    /// </exception>
    public CppBridgeArtifact(
        string relativePath,
        IEnumerable<CppBridgeOperation> operations,
        bool exposeToBindings = false
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(operations);
        this.relativePath = relativePath;
        CppBridgeOperation[] copy = operations.ToArray();
        foreach (CppBridgeOperation operation in copy)
            ArgumentNullException.ThrowIfNull(operation);
        this.operations = Array.AsReadOnly(copy);
        this.exposeToBindings = exposeToBindings;
    }

    /// <summary>
    /// Gets the output path relative to the native output root.
    /// </summary>
    public string relativePath { get; }
    /// <summary>
    /// Gets the frozen source operations, retaining no parser or emitter objects.
    /// </summary>
    public IReadOnlyList<CppBridgeOperation> operations { get; }
    /// <summary>
    /// Gets whether the analyzed binding umbrella must expose this native header.
    /// </summary>
    public bool exposeToBindings { get; }
}
