
namespace BGCS.Contracts.Targets;

/// <summary>Declares parser SDK and consumer module capabilities, independently of build tools or certification.</summary>
/// <param name="requiresExplicitSdk">Whether parsing this target requires an explicitly selected SDK.</param>
/// <param name="supportsDynamicModules">Whether the consumer runtime can load native modules dynamically.</param>
public sealed record TargetCapabilities(
    bool requiresExplicitSdk,
    bool supportsDynamicModules
);

/// <summary>Names an independently verified platform support stage.</summary>
public enum PlatformVerificationStage
{
    /// <summary>Target and toolchain facts have been resolved.</summary>
    Resolved,
    /// <summary>Real target headers have been parsed.</summary>
    Parsed,
    /// <summary>Complete native and managed source artifacts have been generated.</summary>
    Generated,
    /// <summary>The generated managed source has compiled.</summary>
    ManagedCompiled,
    /// <summary>Native construction and linking have succeeded.</summary>
    NativeLinked,
    /// <summary>A real target application has invoked the native fixture.</summary>
    Invoked,
    /// <summary>Clean package installation and consumption have succeeded.</summary>
    Packaged
}
