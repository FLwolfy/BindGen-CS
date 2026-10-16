using System.Collections.Immutable;
using BGCS.Contracts.Common;

namespace BGCS.Contracts.Bridge;

/// <summary>Plans real C exports for selected header-defined helpers; the adapter must be compiled into the configured native module.</summary>
/// <param name="exportPrefix">The validated C export prefix.</param>
/// <param name="exports">The complete helper export mappings in deterministic order.</param>
public sealed record CHeaderBridgeModule(
    string exportPrefix,
    ImmutableArray<CHeaderBridgeExport> exports
);

/// <summary>Preserves the exact adjusted C callable type independently of its new linker symbol.</summary>
/// <param name="sourceDeclaration">The original helper declaration identity.</param>
/// <param name="sourceName">The original C identifier invoked by the adapter.</param>
/// <param name="exportName">The actual exported wrapper symbol.</param>
/// <param name="returnType">The original native return type.</param>
/// <param name="parameters">The original adjusted native parameter types.</param>
public sealed record CHeaderBridgeExport(
    StableId sourceDeclaration,
    string sourceName,
    string exportName,
    StableId returnType,
    ImmutableArray<CHeaderBridgeParameter> parameters
);

/// <summary>Retains a helper parameter's source identity and exact C ABI type.</summary>
/// <param name="sourceDeclaration">The original parameter identity.</param>
/// <param name="type">The adjusted native parameter type.</param>
public sealed record CHeaderBridgeParameter(
    StableId sourceDeclaration,
    StableId type
);
