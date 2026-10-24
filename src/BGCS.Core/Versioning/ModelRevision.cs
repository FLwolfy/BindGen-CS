namespace BGCS.Core.Versioning;

/// <summary>Defines explicit frozen model and transport revisions independently of package versions.</summary>
public static class ModelRevision
{
    /// <summary>The detached native fact graph serialization revision.</summary>
    public const int C_NATIVE = 4;
    /// <summary>The typed C++ bridge operation graph serialization revision.</summary>
    public const int C_NATIVE_BRIDGE = 2;
    /// <summary>The length-framed frontend process protocol revision.</summary>
    public const int C_FRONTEND_PROTOCOL = 3;
}
