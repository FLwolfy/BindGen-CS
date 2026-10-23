using BGCS.Core.Common;

namespace BGCS.Core.Calls;

/// <summary>Describes a provider's explicit physical call decision.</summary>
public enum NativeCallDisposition
{
    /// <summary>The requested ABI identity is not owned by this provider.</summary>
    NotApplicable,
    /// <summary>The exact signature has a complete direct-call plan.</summary>
    Direct,
    /// <summary>The signature requires a native pointer or semantic bridge.</summary>
    RequiresBridge,
    /// <summary>The signature or execution mechanism cannot be implemented.</summary>
    Unsupported
}

/// <summary>Records an ABI decision without promoting capabilities into verified evidence.</summary>
/// <param name="provider">The stable ABI implementation identity.</param>
/// <param name="disposition">The exact request's decision.</param>
/// <param name="reason">The capability or rejection explanation.</param>
/// <param name="plan">The complete direct plan, present only for Direct.</param>
public sealed record CallAbiAssessment(
    CallAbiId provider,
    NativeCallDisposition disposition,
    string reason,
    NativeCallPlan? plan = null
)
{
    /// <summary>Gets the exact failed native value identity, or null for a profile-wide or signature-wide rejection.</summary>
    public StableId? rejectionType { get; init; }
}
