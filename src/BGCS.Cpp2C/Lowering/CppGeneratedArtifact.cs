namespace BGCS.Cpp2C.Lowering;

/// <summary>An immutable source or resource contributed by a lowering plugin.</summary>
/// <param name="relativePath">
/// Normalized relative destination within the generated output.
/// </param>
/// <param name="content">
/// Complete source or resource text.
/// </param>
/// <param name="kind">
/// Destination and compilation role.
/// </param>
/// <param name="exposeToBindings">
/// Whether declarations in this public header enter C# binding generation.
/// </param>
/// <param name="safety">
/// Evidence level required by the contributed content.
/// </param>
public sealed record CppGeneratedArtifact(
    string relativePath,
    string content,
    CppGeneratedArtifactKind kind,
    bool exposeToBindings = false,
    CppLoweringSafety safety = CppLoweringSafety.Verified
);
