using System.Collections.Immutable;
using BGCS.Core.Common;

namespace BGCS.Core.Bridge;

/// <summary>Declares user-owned C ABI headers and implementation inputs for one explicit native shim.</summary>
public sealed record NativeShimRequest
{
    /// <summary>Gets the unique shim input identity.</summary>
    public string id { get; init; } = string.Empty;
    /// <summary>Gets public headers interpreted through the ordinary worker parse request.</summary>
    public ImmutableArray<string> publicHeaders { get; init; } = [];
    /// <summary>Gets native implementation sources published for the consumer's own compilation.</summary>
    public ImmutableArray<string> sourceFiles { get; init; } = [];
    /// <summary>Gets exact C-linkage symbols declared by the public headers.</summary>
    public ImmutableArray<string> exports { get; init; } = [];
    /// <summary>Gets the explicit user assertion associated with this shim's semantics.</summary>
    public Evidence evidence { get; init; } = new(EvidenceLevel.UserAsserted, "native-shim");
}

/// <summary>Stores normalized explicit shim files and symbols without reading them or executing a compiler.</summary>
/// <param name="id">The declared shim identity.</param>
/// <param name="publicHeaders">Absolute public header inputs.</param>
/// <param name="sourceFiles">Absolute native implementation inputs.</param>
/// <param name="exports">Exact explicitly selected C symbols.</param>
/// <param name="evidence">The explicit semantic assertion preserved for inspection.</param>
public sealed record NativeShimPlan(
    string id,
    ImmutableArray<string> publicHeaders,
    ImmutableArray<string> sourceFiles,
    ImmutableArray<string> exports,
    Evidence evidence
);
