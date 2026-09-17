namespace BGCS.Core.IO;

/// <summary>
/// Stages a complete output directory and replaces the destination only after an explicit commit.
/// </summary>
public sealed class OutputDirectoryTransaction : IDisposable
{
    private readonly string destinationPath;
    private bool committed;

    /// <summary>
    /// Creates a staging directory on the same volume as the destination.
    /// </summary>
    /// <param name="destinationPath">Final output directory replaced by <see cref="Commit"/>.</param>
    public OutputDirectoryTransaction(string destinationPath)
    {
        this.destinationPath = Path.GetFullPath(destinationPath);
        string parent = Path.GetDirectoryName(this.destinationPath)
            ?? throw new ArgumentException("The output path must have a parent directory.", nameof(destinationPath));
        Directory.CreateDirectory(parent);
        string name = Path.GetFileName(this.destinationPath);
        StagingPath = Path.Combine(parent, $".{name}.bgcs-staging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(StagingPath);
    }

    /// <summary>
    /// Gets the directory into which all candidate output must be written.
    /// </summary>
    public string StagingPath { get; }

    /// <summary>
    /// Atomically promotes the staged directory and removes the previous successful output.
    /// </summary>
    public void Commit()
    {
        if (committed)
        {
            throw new InvalidOperationException("The output directory transaction has already been committed.");
        }
        if (File.Exists(destinationPath))
        {
            throw new IOException($"The output path is an existing file: {destinationPath}");
        }

        string? backupPath = null;
        if (Directory.Exists(destinationPath))
        {
            backupPath = destinationPath + ".bgcs-backup-" + Guid.NewGuid().ToString("N");
            Directory.Move(destinationPath, backupPath);
        }

        try
        {
            Directory.Move(StagingPath, destinationPath);
            committed = true;
            if (backupPath != null)
            {
                Directory.Delete(backupPath, true);
            }
        }
        catch
        {
            if (backupPath != null && !Directory.Exists(destinationPath) && Directory.Exists(backupPath))
            {
                Directory.Move(backupPath, destinationPath);
            }
            throw;
        }
    }

    /// <summary>
    /// Removes uncommitted staged output.
    /// </summary>
    public void Dispose()
    {
        if (!committed && Directory.Exists(StagingPath))
        {
            Directory.Delete(StagingPath, true);
        }
    }
}
