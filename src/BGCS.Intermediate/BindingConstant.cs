namespace BGCS.Intermediate;

/// <summary>Identifies the normalized value category of a native constant.</summary>
public enum BindingConstantKind
{
    /// <summary>
    /// A signed integral constant represented as an integer.
    /// </summary>
    Integer,
    /// <summary>
    /// An unsigned integral constant represented as an integer.
    /// </summary>
    UnsignedInteger,
    /// <summary>
    /// A signed constant represented as a wide integer.
    /// </summary>
    LongInteger,
    /// <summary>
    /// An unsigned constant represented as a wide integer.
    /// </summary>
    UnsignedLongInteger,
    /// <summary>
    /// A binary floating-point constant.
    /// </summary>
    FloatingPoint,
    /// <summary>
    /// A decimal managed constant.
    /// </summary>
    Decimal,
    /// <summary>
    /// An encoded native text constant normalized to managed text.
    /// </summary>
    String,
    /// <summary>
    /// A constant expression referring to another generated constant.
    /// </summary>
    Reference,
    /// <summary>
    /// A consumer-supplied constant expression and carrier.
    /// </summary>
    Custom
}

/// <summary>Represents a native compile-time constant after preprocessing and naming analysis.</summary>
/// <param name = "nativeName">Native macro or constant name.</param>
/// <param name = "managedName">Managed constant name.</param>
/// <param name = "managedType">Managed compile-time type.</param>
/// <param name = "value">Normalized C# constant expression.</param>
/// <param name = "kind">Normalized value category.</param>
public sealed record BindingConstant(
    string nativeName,
    string managedName,
    string managedType,
    string value,
    BindingConstantKind kind
);
