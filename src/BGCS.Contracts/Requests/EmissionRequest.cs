using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Targets;
using System.Collections.Immutable;

namespace BGCS.Contracts.Requests;

/// <summary>Identifies candidate artifact storage and backend execution choices without granting publication ownership.</summary>
/// <param name="libraryName">The native import library name.</param>
/// <param name="sink">The caller-owned candidate artifact store.</param>
public sealed record EmissionRequest(
    string libraryName,
    IArtifactSink sink
)
{
    /// <summary>Gets an optional immutable external/framework reference and source closure for managed compile-time assertions.</summary>
    public ManagedCompilationInputs? managedCompilation { get; init; }
    /// <summary>Gets explicit namespaces imported by binding artifacts; Runtime source remains independently qualified.</summary>
    public ImmutableArray<string> additionalUsings { get; init; } = [];
    /// <summary>Gets the selected import mechanism.</summary>
    public BindingImportMode importMode { get; init; } = BindingImportMode.FunctionTable;
    /// <summary>Gets the generated consumer framework.</summary>
    public string framework { get; init; } = "net10.0";
    /// <summary>Gets resolved native symbol acquisition, AOT and disposal constraints, or null for a direct desktop backend request.</summary>
    public ManagedRuntimeProfile? managedRuntime { get; init; }
    /// <summary>Gets the exact generated static native table protocol when the selected profile requires its adapter.</summary>
    public StaticExportTableDescriptor? staticExportTable { get; init; }
    /// <summary>Gets the runtime namespace used by source or package references.</summary>
    public string runtimeNamespace { get; init; } = "BGCS.Runtime";
    /// <summary>Gets whether the selected runtime source closure is emitted alongside the API.</summary>
    public bool includeRuntimeSource { get; init; }
    /// <summary>Gets explicitly requested Runtime source features in addition to the binding's minimal dependency closure.</summary>
    public ImmutableArray<string> additionalRuntimeSourceFeatures { get; init; } = [];
    /// <summary>Gets whether managed sources are composed into one artifact.</summary>
    public bool singleFile { get; init; }
    /// <summary>Gets the optional single managed source name.</summary>
    public string singleFileName { get; init; } = "Bindings.cs";
    /// <summary>Gets the positive code-unit copy bound for native value strings destroyed within their creating call.</summary>
    public int maximumValueStringCodeUnits { get; init; } = 1048576;
}
