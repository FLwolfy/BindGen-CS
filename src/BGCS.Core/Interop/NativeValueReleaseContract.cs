using BGCS.Core.Common;

namespace BGCS.Core.Interop;

/// <summary>Restricts a finite native value cleanup capability without promising arbitrary disposal affinity.</summary>
public enum NativeValueReleaseScope
{
    /// <summary>Establishes no creating-call or other cleanup capability.</summary>
    Unknown,
    /// <summary>Allows a fresh owned value to be copied and destroyed synchronously within the same creating call and caller thread.</summary>
    CreatingCall
}

/// <summary>Declares cleanup usable only by a synchronous creating-call projection that cannot expose caller-owned aliases.</summary>
/// <param name="scope">The finite cleanup scope; it does not authorize escaping handles, finalizers or other threads.</param>
/// <param name="evidence">The independently retained exact standard-type provenance.</param>
public sealed record NativeValueReleaseContract(
    NativeValueReleaseScope scope,
    Evidence evidence
);
