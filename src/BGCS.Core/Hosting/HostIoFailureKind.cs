namespace BGCS.Core.Hosting;

/// <summary>Describes a host-classified file operation failure without exposing native error numbers to core mechanisms.</summary>
public enum HostIoFailureKind
{
    /// <summary>The operation must propagate the original failure.</summary>
    Permanent,
    /// <summary>An existing reader or writer temporarily retains exclusive access.</summary>
    SharingViolation,
    /// <summary>The host permits the existing bounded publication-move retry policy.</summary>
    TemporaryAccess
}
