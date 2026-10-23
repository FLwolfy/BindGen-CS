using System.Threading;
using System.Threading.Tasks;

namespace BGCS.Core.Artifacts;

/// <summary>Reads and replaces declared candidate text while retaining owner-controlled path and content checks.</summary>
public interface IArtifactEditor : IArtifactReader
{
    /// <summary>Replaces one existing text artifact without changing its relative path or media identity.</summary>
    /// <param name="relativePath">The exact declared artifact identity.</param>
    /// <param name="content">The complete replacement UTF-8 text.</param>
    /// <param name="cancellationToken">Cancels replacement before the new candidate is installed.</param>
    /// <returns>The owner's updated artifact descriptor.</returns>
    ValueTask<ArtifactDescriptor> ReplaceTextAsync(
        string relativePath,
        string content,
        CancellationToken cancellationToken
    );
    /// <summary>Adds an additional text candidate inside the processing stage's owned output prefix.</summary>
    /// <param name="relativePath">A new portable path under the stage's owned prefix.</param>
    /// <param name="mediaType">The declared text or JSON content identity.</param>
    /// <param name="content">The complete UTF-8 candidate text.</param>
    /// <param name="cancellationToken">Cancels addition before installation.</param>
    /// <returns>The owner's complete artifact descriptor.</returns>
    ValueTask<ArtifactDescriptor> AddTextAsync(
        string relativePath,
        string mediaType,
        string content,
        CancellationToken cancellationToken
    );
    /// <summary>Removes one declared text candidate while final contract validation retains authority over completeness.</summary>
    /// <param name="relativePath">The exact declared artifact identity.</param>
    /// <param name="cancellationToken">Cancels removal before the candidate changes.</param>
    /// <returns>The completed owner-controlled removal.</returns>
    ValueTask DeleteTextAsync(
        string relativePath,
        CancellationToken cancellationToken
    );
}
