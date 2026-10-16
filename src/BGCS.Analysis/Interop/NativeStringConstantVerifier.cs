using System;
using System.Linq;
using BGCS.Contracts.Common;
using BGCS.Contracts.Native;

namespace BGCS.Analysis.Interop;

/// <summary>Checks detached literal units without decoding unknown native execution encodings.</summary>
public sealed class NativeStringConstantVerifier
{
    /// <summary>Rejects invalid widths, unit ranges, terminators and unsupported provenance.</summary>
    /// <param name="value">The immutable literal facts.</param>
    public void Verify(NativeStringConstant value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!Enum.IsDefined(value.kind) || value.unitWidth is not (1 or 2 or 4)
            || value.codeUnits.IsDefaultOrEmpty || value.codeUnits.Length > 8 * 1024 * 1024 || value.codeUnits[^1] != 0
            || value.kind is NativeStringLiteralKind.Ordinary or NativeStringLiteralKind.Utf8 && value.unitWidth != 1
            || value.kind == NativeStringLiteralKind.Utf16 && value.unitWidth != 2
            || value.kind == NativeStringLiteralKind.Utf32 && value.unitWidth != 4
            || value.kind == NativeStringLiteralKind.Wide && value.unitWidth is not (2 or 4)
            || value.unitWidth < 4 && value.codeUnits.Any(unit => unit >= (1U << (value.unitWidth * 8)))
            || value.evidence is not { level: EvidenceLevel.Declared or EvidenceLevel.UserAsserted or EvidenceLevel.Verified }
            || string.IsNullOrWhiteSpace(value.evidence.origin) || value.literalTokens.IsDefault || value.literalTokens.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("A native string constant requires exact bounded code units, its final terminator, a valid literal width and explicit evidence.");
    }
}
