namespace BGCS.Cpp2C.Lowering;

/// <summary>Shape of the stable C ABI produced for one C++ value.</summary>
public enum CppAbiShape
{
    /// <summary>
    /// One C-compatible value.
    /// </summary>
    Direct,
    /// <summary>
    /// A data pointer and element count.
    /// </summary>
    PointerAndLength,
    /// <summary>
    /// An explicit presence flag and value.
    /// </summary>
    OptionalValue,
    /// <summary>
    /// An opaque object handle.
    /// </summary>
    OpaqueHandle,
    /// <summary>
    /// An extension-defined group of ABI parameters.
    /// </summary>
    Custom
}
