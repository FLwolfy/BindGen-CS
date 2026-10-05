using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BGCS.Analysis;
using BGCS.Configuration;
using BGCS.Core.IO;
using BGCS.Metadata;

namespace BGCS.Patching;

/// <summary>
/// Runs ordered analysis transformations and atomically publishes transformed managed output.
/// </summary>
public sealed class PatchEngine
{
    private readonly List<IPrePatch> m_preGenerationPatches = [];
    private readonly List<IPostPatch> m_postGenerationPatches = [];

    /// <summary>
    /// Creates empty ordered patch catalogs without allocating filesystem state.
    /// </summary>
    public PatchEngine()
    {
    }

    /// <summary>
    /// Registers an analysis transformation after the currently registered pre-patches.
    /// </summary>
    /// <param name="patch">
    /// The transformation retained until this engine is released.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// The transformation is null.
    /// </exception>
    public void RegisterPrePatch(IPrePatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        m_preGenerationPatches.Add(patch);
    }

    /// <summary>
    /// Registers an output transformation after the currently registered post-patches.
    /// </summary>
    /// <param name="patch">
    /// The transformation retained until this engine is released.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// The transformation is null.
    /// </exception>
    public void RegisterPostPatch(IPostPatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        m_postGenerationPatches.Add(patch);
    }

    /// <summary>
    /// Runs pre-patches against the borrowed analysis model without publishing source-file changes.
    /// </summary>
    /// <param name="settings">
    /// The active mutable generation configuration.
    /// </param>
    /// <param name="result">
    /// The model owned by the current generation attempt.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// The configuration or model is null.
    /// </exception>
    public void ApplyPrePatches(
        CsCodeGeneratorConfig settings,
        ParseResult result
    ) {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(result);
        foreach (IPrePatch patch in m_preGenerationPatches)
        {
            patch.Apply(settings, result);
        }
    }

    /// <summary>
    /// Runs all post-patches within one owned output transaction and replaces output only after success.
    /// </summary>
    /// <param name="metadata">
    /// The mutable generated metadata shared by the ordered transformations.
    /// </param>
    /// <param name="outputDir">
    /// The existing output tree to snapshot and replace. Unselected files are preserved.
    /// </param>
    /// <param name="files">
    /// The selected full source paths inside the output tree, converted to relative paths for patches.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// The metadata or selected file collection is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// A selected file is outside the output tree.
    /// </exception>
    /// <exception cref="IOException">
    /// Snapshot, transformation, or publication IO fails; the transaction preserves previous output.
    /// </exception>
    public void ApplyPostPatches(
        CsCodeGeneratorMetadata metadata,
        string outputDir,
        List<string> files
    ) {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(files);
        if (m_postGenerationPatches.Count == 0)
        {
            return;
        }
        string root = Path.GetFullPath(outputDir);
        using OutputDirectoryTransaction transaction = new(root);
        PatchContext context = new(transaction.stagingPath);
        context.CopyFromInput(root);
        List<string> relativeFiles = files.Select(file => Path.GetRelativePath(root, Path.GetFullPath(file))).ToList();
        foreach (string relativeFile in relativeFiles)
        {
            context.GetFullPath(relativeFile);
        }
        foreach (IPostPatch patch in m_postGenerationPatches)
        {
            patch.Apply(context, metadata, relativeFiles);
            foreach (string writtenFile in context.writtenFiles)
            {
                if (!relativeFiles.Contains(writtenFile, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
                    relativeFiles.Add(writtenFile);
            }
        }
        transaction.Commit();
    }
}
