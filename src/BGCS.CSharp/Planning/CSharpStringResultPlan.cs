using BGCS.Core.Interop;

namespace BGCS.CSharp.Planning;

/// <summary>Plans one direct native text copy with independently checked ownership and extent.</summary>
/// <param name="methodIndex">The exact raw import index.</param>
/// <param name="signature">The checked public forwarding signature.</param>
/// <param name="encoding">The explicit native UTF encoding.</param>
/// <param name="codeUnitWidth">The native code-unit width.</param>
/// <param name="byteOrder">The declared code-unit byte order.</param>
/// <param name="allowsNull">Whether a null result becomes a nullable managed string.</param>
/// <param name="maximumCodeUnits">The configured scan or copy cap, not an independent allocation-validity proof.</param>
public sealed record CSharpStringResultPlan(
    int methodIndex,
    CSharpFacadeMethodPlan signature,
    NativeTextEncoding encoding,
    int codeUnitWidth,
    NativeTextByteOrder byteOrder,
    bool allowsNull,
    int maximumCodeUnits
)
{
    /// <summary>Gets a normalized exact code-unit count, or null for a terminator or parameter channel.</summary>
    public int? exactCodeUnits { get; init; }
    /// <summary>Gets a checked native input or output length channel.</summary>
    public CSharpStringResultLengthPlan? length { get; init; }
    /// <summary>Gets the exact independent void(T*) release import, or null for module-lifetime borrowed storage.</summary>
    public int? releaseIndex { get; init; }
}

/// <summary>Plans one scalar native extent without re-evaluating conversion expressions or exposing temporary output storage.</summary>
/// <param name="parameterIndex">The exact native parameter index.</param>
/// <param name="carrierType">The raw integral count carrier.</param>
/// <param name="output">Whether a private count local is passed by address.</param>
/// <param name="unit">The native length unit.</param>
/// <param name="alignment">The native output-count alignment checked before calling.</param>
public sealed record CSharpStringResultLengthPlan(
    int parameterIndex,
    string carrierType,
    bool output,
    NativeLengthUnit unit,
    int alignment
);
