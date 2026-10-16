using BGCS.Contracts.Common;

namespace BGCS.Contracts.Interop;

/// <summary>Identifies the native event or owner that ends a resource's validity.</summary>
public enum NativeLifetimeKind
{
    /// <summary>The validity end is unknown.</summary>
    Unknown,
    /// <summary>Validity ends when one native call returns.</summary>
    Call,
    /// <summary>Validity is bounded by an explicitly identified owner.</summary>
    Owner,
    /// <summary>Validity ends after native registration quiescence.</summary>
    Registration,
    /// <summary>Validity ends after an operation's terminal native acknowledgement.</summary>
    Operation,
    /// <summary>Validity is guaranteed for the process or statically linked module.</summary>
    Static,
    /// <summary>The address remains valid until the supplying native module is actually unloaded.</summary>
    Module
}

/// <summary>Identifies native access to a parameter's storage.</summary>
public enum NativeParameterDirection
{
    /// <summary>Native access direction is unknown.</summary>
    Unknown,
    /// <summary>Native code only reads the supplied storage.</summary>
    Input,
    /// <summary>Native code writes output storage.</summary>
    Output,
    /// <summary>Native code reads and writes supplied storage.</summary>
    InputOutput
}

/// <summary>Describes the owner or terminal protocol that bounds native resource validity.</summary>
/// <param name="kind">The validity boundary.</param>
/// <param name="owner">The owning parameter, object or operation identity when required.</param>
/// <param name="evidence">The declared or verified source of the lifetime fact.</param>
public sealed record LifetimeContract(
    NativeLifetimeKind kind,
    StableId? owner,
    Evidence evidence
);
