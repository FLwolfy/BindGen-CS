using System.Collections.Immutable;

namespace BGCS.Core.Requests;

/// <summary>Names a versioned static native export adapter independently of native or managed source emitters.</summary>
/// <param name="symbolPrefix">The portable collision-resistant adapter symbol prefix.</param>
/// <param name="getExportSymbol">The uint32 index to uintptr function-address entry point.</param>
/// <param name="getRevisionSymbol">The uint32 protocol-revision entry point.</param>
/// <param name="getCountSymbol">The uint32 export-count entry point.</param>
/// <param name="getAbiIdentitySymbol">The zero-terminated 64-character ASCII ABI digest entry point.</param>
/// <param name="getPointerSizeSymbol">The uint32 native pointer-size entry point.</param>
/// <param name="abiIdentity">The exact deterministic ABI digest expected before address resolution.</param>
/// <param name="exportNames">The exact ABI symbols in adapter index order.</param>
public sealed record StaticExportTableDescriptor(
    string symbolPrefix,
    string getExportSymbol,
    string getRevisionSymbol,
    string getCountSymbol,
    string getAbiIdentitySymbol,
    string getPointerSizeSymbol,
    string abiIdentity,
    ImmutableArray<string> exportNames
)
{
    /// <summary>Gets the only supported adapter protocol revision.</summary>
    public int revision { get; init; } = 1;
}
