using BGCS.Contracts.Common;

namespace BGCS.CSharp.Planning;

/// <summary>Plans one direct native pointer return with independently declared matching resource cleanup.</summary>
/// <param name="methodIndex">The unchanged raw import index.</param>
/// <param name="ownership">The exact release export and domain.</param>
public sealed record CSharpDirectOwnedFactoryPlan(
    int methodIndex,
    CSharpOwnedResultPlan ownership
);

/// <summary>Plans an explicit C receiver without inventing source-language membership.</summary>
/// <param name="methodIndex">The unchanged raw import index.</param>
/// <param name="receiverIndex">The exact native receiver slot.</param>
/// <param name="contextSignature">The typed context facade used by aliases, views and shared contexts.</param>
/// <param name="objectSignature">The owned wrapper method with the receiver hidden.</param>
/// <param name="requiredReleaseDomain">An explicit allocation/release domain restriction, or null.</param>
public sealed record CSharpReceiverMethodPlan(
    int methodIndex,
    int receiverIndex,
    CSharpFacadeMethodPlan contextSignature,
    CSharpFacadeMethodPlan objectSignature,
    string? requiredReleaseDomain
);

/// <summary>Records an unsupported ownership convenience while preserving its native raw ABI.</summary>
/// <param name="nativeExport">The native callable.</param>
/// <param name="parameter">An optional receiver parameter.</param>
/// <param name="source">The original source location.</param>
/// <param name="reason">The missing ownership, storage or lifetime requirement.</param>
public sealed record CSharpOwnedProjectionOmissionPlan(
    StableId nativeExport,
    StableId? parameter,
    SourceLocation? source,
    string reason
);
