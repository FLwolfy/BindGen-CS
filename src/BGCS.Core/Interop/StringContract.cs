using BGCS.Core.Common;

namespace BGCS.Core.Interop;

/// <summary>Identifies a declared native text encoding rather than inferring it from pointer spelling.</summary>
public enum NativeTextEncoding
{
    /// <summary>The encoding is unknown.</summary>
    Unknown,
    /// <summary>UTF-8 code units.</summary>
    Utf8,
    /// <summary>UTF-16 code units.</summary>
    Utf16,
    /// <summary>UTF-32 code units.</summary>
    Utf32
}

/// <summary>Declares the byte order of multibyte native text code units.</summary>
public enum NativeTextByteOrder
{
    /// <summary>The code-unit byte order is unknown.</summary>
    Unknown,
    /// <summary>Code units use the consuming native process's byte order.</summary>
    Native,
    /// <summary>Code units are stored in little-endian order.</summary>
    LittleEndian,
    /// <summary>Code units are stored in big-endian order.</summary>
    BigEndian
}

/// <summary>Distinguishes native lengths that cannot be interchanged implicitly.</summary>
public enum NativeLengthUnit
{
    /// <summary>The unit is unknown.</summary>
    Unknown,
    /// <summary>Length is measured in bytes.</summary>
    Bytes,
    /// <summary>Length is measured in native encoding code units.</summary>
    CodeUnits,
    /// <summary>Length is measured in Unicode scalar values.</summary>
    UnicodeScalars,
    /// <summary>Length is measured in typed buffer elements.</summary>
    Elements
}

/// <summary>Describes native text storage and its complete validity requirements.</summary>
/// <param name="encoding">The declared encoding.</param>
/// <param name="codeUnitWidth">The native code-unit width in bytes.</param>
/// <param name="lengthUnit">The native length unit.</param>
/// <param name="direction">Native read and write access.</param>
/// <param name="lifetime">The pointer validity boundary.</param>
/// <param name="evidence">The source of the string contract.</param>
public sealed record StringContract(
    NativeTextEncoding encoding,
    int codeUnitWidth,
    NativeLengthUnit lengthUnit,
    NativeParameterDirection direction,
    LifetimeContract lifetime,
    Evidence evidence
)
{
    /// <summary>Gets explicit multibyte code-unit byte order; one-byte UTF-8 does not require it.</summary>
    public NativeTextByteOrder byteOrder { get; init; }
    /// <summary>Gets zero termination, or null when termination is unknown.</summary>
    public bool? zeroTerminated { get; init; }
    /// <summary>Gets pointer retention, or null when retention is unknown.</summary>
    public bool? retainsPointer { get; init; }
    /// <summary>Gets native null allowance, or null when it is unknown.</summary>
    public bool? allowsNull { get; init; }
    /// <summary>Gets the parameter that supplies the native length.</summary>
    public StableId? lengthParameter { get; init; }
    /// <summary>Gets the exact native length in the declared units when no parameter channel supplies it.</summary>
    public int? fixedLength { get; init; }
    /// <summary>Gets the explicit scalar-input or integral-pointer-output length channel direction.</summary>
    public NativeParameterDirection lengthDirection { get; init; }
    /// <summary>Gets whether an output length pointer is written synchronously and never retained after this call.</summary>
    public bool? lengthOutputCallBounded { get; init; }
    /// <summary>Gets the bounded scan or copy cap in code units; this does not independently prove native storage validity.</summary>
    public int maximumCodeUnits { get; init; } = 1048576;
    /// <summary>Gets explicit ownership and allocator information when native code owns text storage.</summary>
    public OwnershipContract? ownership { get; init; }
}
