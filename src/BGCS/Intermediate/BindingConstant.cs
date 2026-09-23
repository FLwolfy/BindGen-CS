namespace BGCS.Intermediate;

/// <summary>Identifies the normalized value category of a native constant.</summary>
public enum BindingConstantKind
{
    Integer,
    UnsignedInteger,
    LongInteger,
    UnsignedLongInteger,
    FloatingPoint,
    Decimal,
    String,
    Reference,
    Custom
}

/// <summary>Represents a native compile-time constant after preprocessing and naming analysis.</summary>
/// <param name="NativeName">Native macro or constant name.</param>
/// <param name="ManagedName">Managed constant name.</param>
/// <param name="ManagedType">Managed compile-time type.</param>
/// <param name="Value">Normalized C# constant expression.</param>
/// <param name="Kind">Normalized value category.</param>
public sealed record BindingConstant(string NativeName, string ManagedName, string ManagedType, string Value,
    BindingConstantKind Kind);
