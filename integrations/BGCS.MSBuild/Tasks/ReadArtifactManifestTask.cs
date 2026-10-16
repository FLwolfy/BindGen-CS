using System;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace BGCS.MSBuild;

/// <summary>Reads exactly the hash-verified publication under the Host publication-lock protocol.</summary>
public sealed class ReadArtifactManifestTask : Task
{
    /// <summary>Gets or sets the absolute output root of one inner build.</summary>
    [Required]
    public string OutputDirectory { get; set; } = string.Empty;
    /// <summary>Gets or sets the expected logical publication owner.</summary>
    [Required]
    public string LibraryId { get; set; } = string.Empty;
    /// <summary>Gets exact managed compilation items from the complete verified artifact collection.</summary>
    [Output]
    public ITaskItem[] GeneratedFiles { get; private set; } = [];

    /// <inheritdoc/>
    public override bool Execute()
    {
        try
        {
            string root = Path.GetFullPath(OutputDirectory);
            var artifacts = BindingArtifactManifest.Read(root, LibraryId);
            if (artifacts.IsEmpty) throw new IOException("The generated output has no complete owned artifact collection.");
            GeneratedFiles = artifacts.Where(artifact => artifact.mediaType == "text/x-csharp").Select(artifact =>
            {
                TaskItem item = new(Path.GetFullPath(artifact.relativePath, root));
                item.SetMetadata("AutoGen", "true");
                item.SetMetadata("Visible", "false");
                return (ITaskItem)item;
            }).ToArray();
            return true;
        }
        catch (Exception exception)
        {
            Log.LogErrorFromException(exception, showStackTrace: false);
            return false;
        }
    }
}
