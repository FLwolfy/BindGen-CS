namespace BGCS.Contracts.Versioning;

/// <summary>Defines explicit frozen model and transport revisions independently of package versions.</summary>
public static class ModelRevision
{
    /// <summary>The detached native fact graph serialization revision.</summary>
    public const int C_NATIVE = 1;
    /// <summary>The typed C++ bridge operation graph serialization revision.</summary>
    public const int C_CPP_BRIDGE = 1;
    /// <summary>The length-framed frontend process protocol revision.</summary>
    public const int C_FRONTEND_PROTOCOL = 1;
}
