namespace BGCS.Core.Inputs;

/// <summary>Captures the effective directory name-comparison fact used to interpret a declared input path.</summary>
/// <param name="directory">The absolute parent or ancestor directory name; an absent tail uses its nearest existing owner.</param>
/// <param name="caseSensitive">The observed effective child-name comparison behavior.</param>
public sealed record InputPathSemantics(
    string directory,
    bool caseSensitive
);
