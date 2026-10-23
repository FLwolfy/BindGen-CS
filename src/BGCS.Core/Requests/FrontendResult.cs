using BGCS.Core.Inputs;
using BGCS.Core.Native;
using System.Collections.Immutable;

namespace BGCS.Core.Requests;

/// <summary>Returns frozen native facts together with independently checkable parse inputs.</summary>
/// <param name="module">The detached native fact graph.</param>
/// <param name="snapshot">The exact file bytes and include-directory membership used for parsing.</param>
public sealed record FrontendResult(
    NativeModule module,
    SourceSnapshot snapshot
)
{
    /// <summary>Gets independently locked extracted parser resources, or null when the frontend has no verifiable execution-input closure.</summary>
    public SourceSnapshot? executionInputs { get; init; }
    /// <summary>Gets authenticated parser builtin headers that belong to the frontend rather than the consumer's native SDK; plugin and user inputs are excluded.</summary>
    public SourceSnapshot? parserBuiltinInputs { get; init; }
    /// <summary>Gets reasons why these facts cannot be restored without a fresh frontend parse.</summary>
    /// <remarks>A downstream emission cache may still reuse the identical fully fingerprinted result of a fresh parse; these reasons do not authorize reuse of the parse itself.</remarks>
    public ImmutableArray<string> cacheIneligibilityReasons { get; init; } = [];
}
