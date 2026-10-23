using System;
using System.Collections.Generic;
using BGCS.Core.Targets;

namespace BGCS;

/// <summary>Resolves each requested native target through the frozen explicit provider composition.</summary>
public sealed partial class BindingHost
{
    private static NativeTargetDescriptor ResolveNativeTarget(
        IEnumerable<INativeTargetProvider> providers,
        NativeTargetRequest request
    ) {
        NativeTargetDescriptor? selected = null;
        foreach (INativeTargetProvider provider in providers)
        {
            if (!provider.TryResolve(request, out NativeTargetDescriptor? candidate)) continue;
            if (candidate is null) throw new InvalidOperationException("A target provider claimed a request without returning detached target facts.");
            if (selected is not null) throw new InvalidOperationException("Multiple target providers claim the explicit request. Select an unambiguous extension composition.");
            if (request.tripleOverride is not null && request.tripleOverride != candidate.triple)
                throw new InvalidOperationException("A target provider replaced the explicitly requested compiler target.");
            selected = candidate;
        }
        return selected ?? throw new NotSupportedException($"No registered target provider owns '{request.targetId.value}'.");
    }
}
