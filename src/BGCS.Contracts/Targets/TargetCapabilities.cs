using System.Collections.Immutable;
using BGCS.Contracts.Requests;

namespace BGCS.Contracts.Targets;

/// <summary>Declares implemented platform construction mechanisms independently of certification evidence.</summary>
/// <param name="outputKinds">Native construction forms supplied by the platform adapter.</param>
/// <param name="defaultOutputKind">The native consumption form selected by an automatic request.</param>
/// <param name="requiresExplicitSdk">Whether the platform adapter requires a target SDK.</param>
/// <param name="supportsDynamicModules">Whether BGCS supplies a dynamic native module path for this target family.</param>
/// <param name="assetLayout">The native asset layout family.</param>
public sealed record TargetCapabilities(
    ImmutableArray<NativeOutputKind> outputKinds,
    NativeOutputKind defaultOutputKind,
    bool requiresExplicitSdk,
    bool supportsDynamicModules,
    string assetLayout
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
