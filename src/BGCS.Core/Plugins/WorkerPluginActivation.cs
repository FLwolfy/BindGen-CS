using System.Collections.Immutable;
using System.Text.Json;
using BGCS.Core.Common;
using BGCS.Core.Inputs;

namespace BGCS.Core.Plugins;

/// <summary>Freezes a worker-only plugin's declared files and settings without loading its implementation in the host.</summary>
/// <param name="manifestPath">The absolute local manifest identity.</param>
/// <param name="manifest">The locked entry, descriptor and complete executable asset closure.</param>
/// <param name="inputs">Content and physical identities captured by manifest discovery.</param>
/// <param name="missingInputs">Absent dependency metadata that must remain absent.</param>
/// <param name="settings">The detached schema-validated settings object.</param>
/// <param name="pathBase">The absolute fallback base used to reconstruct settings.</param>
/// <param name="valuePathBases">Exact JSON-pointer origins for composed settings.</param>
/// <param name="parserIdentity">The exact parser execution identity required by this activation.</param>
public sealed record WorkerPluginActivation(
    string manifestPath,
    PluginManifest manifest,
    ImmutableArray<InputDependency> inputs,
    ImmutableArray<string> missingInputs,
    JsonElement settings,
    string pathBase,
    ImmutableDictionary<string, string> valuePathBases,
    string parserIdentity
);

/// <summary>Records one worker plugin or contributor's implementation and private output-affecting state.</summary>
/// <param name="id">The globally unique plugin or service identity.</param>
/// <param name="implementation">The locked type and executable content identity.</param>
/// <param name="state">Declared private state, or null when complete fingerprinting is unavailable.</param>
public sealed record WorkerPluginFingerprint(
    string id,
    string implementation,
    ContentDigest? state
);

/// <summary>Returns detached pre-parse state and local inputs discovered inside a worker.</summary>
/// <param name="fingerprints">Ordered plugin and contributor identities.</param>
/// <param name="inputs">Complete declared input content, membership and absence observations.</param>
/// <param name="unfingerprintedServices">Services whose affected generation caches must be disabled.</param>
public sealed record WorkerPluginCapture(
    ImmutableArray<WorkerPluginFingerprint> fingerprints,
    SourceSnapshot inputs,
    ImmutableArray<string> unfingerprintedServices
);
