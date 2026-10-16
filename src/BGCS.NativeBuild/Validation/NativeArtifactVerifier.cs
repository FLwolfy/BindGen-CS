using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;

namespace BGCS.NativeBuild.Validation;

/// <summary>Verifies artifact format and target architecture separately from calling-ABI certification.</summary>
public static class NativeArtifactVerifier
{
    /// <summary>Rejects unknown formats, wrong architectures, thin archives and unverified archive members.</summary>
    /// <param name="path">The existing complete native artifact.</param>
    /// <param name="targetId">The concrete native target identity.</param>
    /// <param name="outputKind">The declared native consumption form.</param>
    public static void Verify(
        string path,
        string targetId,
        NativeOutputKind outputKind
    ) => NativeBinaryIdentity.Validate(path, targetId, outputKind);
    /// <summary>Verifies physical binary facts using a resolved descriptor whose logical target ID remains opaque.</summary>
    /// <param name="path">The complete native artifact.</param>
    /// <param name="target">The actual platform, CPU and ABI descriptor.</param>
    /// <param name="outputKind">The native consumption form.</param>
    public static void Verify(
        string path,
        NativeTargetDescriptor target,
        NativeOutputKind outputKind
    ) => NativeBinaryIdentity.Validate(path, target, outputKind);
}
