using System;
using System.Threading;
using System.Threading.Tasks;

namespace BGCS.Core.Artifacts;

/// <summary>Reads only declared immutable candidate artifacts without exposing publication paths.</summary>
public interface IArtifactReader
{
    /// <summary>Reads a complete artifact whose content identity is checked by its owner.</summary>
    /// <param name="relativePath">An exact declared artifact path.</param>
    /// <param name="cancellationToken">Cancels file reading and digest verification.</param>
    /// <returns>The complete verified bytes.</returns>
    ValueTask<ReadOnlyMemory<byte>> ReadBytesAsync(
        string relativePath,
        CancellationToken cancellationToken
    );
    /// <summary>Reads strict UTF-8 text from one declared artifact.</summary>
    /// <param name="relativePath">An exact declared artifact path.</param>
    /// <param name="cancellationToken">Cancels reading and decoding.</param>
    /// <returns>The complete verified text.</returns>
    ValueTask<string> ReadTextAsync(
        string relativePath,
        CancellationToken cancellationToken
    );
}
