using System.Collections.Immutable;
using BGCS.Core.Common;

namespace BGCS.Core.Requests;

/// <summary>Freezes the managed environment used to establish asserted compile-time values without loading consumer assemblies.</summary>
public sealed record ManagedProjectionInputs
{
    /// <summary>Gets the complete explicit, adjacent dependency and framework metadata closure, with immutable original PE bytes.</summary>
    public ImmutableArray<ManagedAssemblyInput> assemblies { get; init; } = [];
    /// <summary>Gets every explicitly declared additional source file, independently of constant selection.</summary>
    public ImmutableArray<ManagedSourceInput> sources { get; init; } = [];
    /// <summary>Gets the actual preprocessor symbols used for all captured source trees.</summary>
    public ImmutableArray<string> defines { get; init; } = [];
}

/// <summary>Preserves exact metadata input bytes; implementations read PE metadata without executing module initializers or user code.</summary>
/// <param name="path">The absolute original input path for diagnostics and provenance.</param>
/// <param name="assemblyIdentity">The independently read managed assembly identity.</param>
/// <param name="digest">The digest of the complete original file bytes.</param>
/// <param name="image">The immutable complete PE image.</param>
/// <param name="isFramework">Whether this file belongs to the executing .NET framework closure.</param>
public sealed record ManagedAssemblyInput(
    string path,
    string assemblyIdentity,
    ContentDigest digest,
    ImmutableArray<byte> image,
    bool isFramework
);

/// <summary>Preserves one source file's original bytes and decoded text for deterministic compiler binding.</summary>
/// <param name="path">The absolute original input path.</param>
/// <param name="digest">The digest of the original bytes, including encoding markers.</param>
/// <param name="image">The immutable original file bytes.</param>
/// <param name="text">Text decoded from those same bytes using their compiler-supported encoding.</param>
public sealed record ManagedSourceInput(
    string path,
    ContentDigest digest,
    ImmutableArray<byte> image,
    string text
);
