namespace BGCS.Intermediate.Bridges;

/// <summary>
/// Records the native layout and C ABI identity of one analyzed bridge type.
/// </summary>
/// <param name = "nativeName">
/// The qualified source type identity, including concrete template arguments.
/// </param>
/// <param name = "cName">
/// The lowered C type identity.
/// </param>
/// <param name = "kind">
/// The ABI role of the type, including opaque object handles.
/// </param>
/// <param name = "size">
/// The native layout size, or zero when incomplete.
/// </param>
/// <param name = "alignment">
/// The native alignment, or zero when incomplete.
/// </param>
public sealed record CppBridgeType(
    string nativeName,
    string cName,
    BindingTypeKind kind,
    int size,
    int alignment
);
