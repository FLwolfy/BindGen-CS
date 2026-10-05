namespace BGCS.Cpp2C.Lowering;

/// <summary>Destination for a generated extension artifact.</summary>
public enum CppGeneratedArtifactKind
{
    /// <summary>
    /// A public C header in the native output.
    /// </summary>
    PublicHeader,
    /// <summary>
    /// A native implementation source in the native output.
    /// </summary>
    NativeSource,
    /// <summary>
    /// A C# source in the managed output.
    /// </summary>
    ManagedSource,
    /// <summary>
    /// An auxiliary resource in the native output.
    /// </summary>
    Resource
}
