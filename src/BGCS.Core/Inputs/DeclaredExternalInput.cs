namespace BGCS.Core.Inputs;

/// <summary>Identifies the content scope of an extension's explicit external input.</summary>
public enum ExternalInputKind
{
    /// <summary>A complete file's bytes and physical identity.</summary>
    File,
    /// <summary>A directory's complete membership, links and recursive file contents.</summary>
    Directory
}

/// <summary>Declares an extension input before a cache lookup or candidate publication.</summary>
/// <param name="path">The local input path resolved from the fingerprint context.</param>
/// <param name="kind">The complete content scope.</param>
/// <param name="allowMissing">Whether absence is a valid tracked input state.</param>
public sealed record DeclaredExternalInput(
    string path,
    ExternalInputKind kind,
    bool allowMissing = false
);
