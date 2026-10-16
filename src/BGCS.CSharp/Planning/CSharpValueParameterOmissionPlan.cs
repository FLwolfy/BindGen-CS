using BGCS.Contracts.Common;

namespace BGCS.CSharp.Planning;

/// <summary>Retains a precise convenience omission without rejecting an independently representable raw pointer ABI.</summary>
/// <param name="nativeExport">The original native operation.</param>
/// <param name="parameter">The exact pointer whose temporary carrier could not be established.</param>
/// <param name="source">The original parameter or callable source location.</param>
/// <param name="reason">The missing managed storage or address proof.</param>
public sealed record CSharpValueParameterOmissionPlan(
    StableId nativeExport,
    StableId parameter,
    SourceLocation? source,
    string reason
);
