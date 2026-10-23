using System.Collections.Immutable;
using BGCS.Core.Common;

namespace BGCS.Core.Native;

/// <summary>Preserves the source-language literal category separately from its exact code units.</summary>
public enum NativeStringLiteralKind
{
    /// <summary>An ordinary execution-character string; bytes are not assumed to be Unicode.</summary>
    Ordinary,
    /// <summary>A UTF-8 string literal.</summary>
    Utf8,
    /// <summary>A UTF-16 string literal.</summary>
    Utf16,
    /// <summary>A UTF-32 string literal.</summary>
    Utf32,
    /// <summary>A target wchar_t string literal whose units remain explicit.</summary>
    Wide
}

/// <summary>Retains exact compiler-established string code units, including embedded and final NUL units.</summary>
/// <param name="kind">The source literal category.</param>
/// <param name="codeUnits">Unsigned code-unit bit values, including the implicit final zero.</param>
/// <param name="unitWidth">The compiler-established code-unit width in bytes.</param>
/// <param name="evidence">The compiler extraction provenance; this is not proof for another native compiler.</param>
public sealed record NativeStringConstant(
    NativeStringLiteralKind kind,
    ImmutableArray<uint> codeUnits,
    int unitWidth,
    Evidence evidence
)
{
    /// <summary>Gets exact original literal token spellings, when available; generated expected bytes must never be substituted for this proof input.</summary>
    public ImmutableArray<string> literalTokens { get; init; } = [];
}
