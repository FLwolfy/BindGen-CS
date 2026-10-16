using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Contracts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Native;

namespace BGCS.Analysis.Safety;

internal static class CallbackSafetyAnalyzer
{
    internal static ImmutableArray<BindingDiagnostic> Analyze(InteropModule module)
    {
        var diagnostics = ImmutableArray.CreateBuilder<BindingDiagnostic>();
        foreach (InteropFunction function in module.functions)
            foreach (InteropParameter parameter in function.parameters)
            {
                if (!IsCallback(module, function, parameter)) continue;
                CallbackContract? callback = parameter.callback;
                if (callback is null || !Explicit(callback.evidence) || callback.retained is null || callback.lifetime is not { } lifetime
                    || !Enum.IsDefined(lifetime.kind) || lifetime.kind == NativeLifetimeKind.Unknown || !Explicit(lifetime.evidence))
                    Add(BindingDiagnosticCodes.C_CALLBACKLIFETIME,
                        "has no complete explicit callback lifetime and retention contract. Declare semantics.callbackParameters with its actual lifetime and retained flag; "
                        + "retained callbacks also require their exact unregister/completion and native-frame quiescence protocol, or supply those facts through an interop plugin.");
                if (callback is null || !Explicit(callback.evidence) || !Enum.IsDefined(callback.threading) || callback.threading == NativeCallbackThreading.Unknown)
                    Add(BindingDiagnosticCodes.C_CALLBACKTHREADING,
                        "has no explicit native callback invocation-thread contract. Declare semantics.callbackParameters.threading for the actual caller, event loop or native threads, "
                        + "or supply that fact through an interop plugin.");

                void Add(
                    string code,
                    string message
                ) {
                    BindingDiagnostic diagnostic = new(BindingDiagnosticSeverity.Warning,
                        $"Native callback parameter '{function.exportName}:{parameter.sourceName ?? parameter.name}' {message} No thread or lifetime is inferred; its raw function-pointer ABI remains available.", code)
                    { location = parameter.source ?? function.source };
                    if (!module.diagnostics.Contains(diagnostic) && !diagnostics.Contains(diagnostic)) diagnostics.Add(diagnostic);
                }
            }
        return diagnostics.ToImmutable();
    }

    private static bool IsCallback(
        InteropModule module,
        InteropFunction function,
        InteropParameter parameter
    ) {
        HashSet<StableId> seen = [];
        InteropType type = Resolve(parameter.type);
        if (type.category != NativeTypeCategory.Pointer) return false;
        if (type.elementType is not StableId element) throw Invalid(type.id);
        return Resolve(element).category == NativeTypeCategory.Function;

        InteropType Resolve(StableId id)
        {
            while (true)
            {
                if (string.IsNullOrWhiteSpace(id.value) || !seen.Add(id)) throw Invalid(id);
                InteropType resolved;
                try { resolved = module.GetType(id); }
                catch (KeyNotFoundException failure) { throw Invalid(id, failure); }
                if (!Enum.IsDefined(resolved.category)) throw Invalid(id);
                if (resolved.category is not (NativeTypeCategory.Alias or NativeTypeCategory.Qualified)) return resolved;
                if (resolved.elementType is not StableId next) throw Invalid(id);
                id = next;
            }
        }
        InvalidOperationException Invalid(
            StableId id,
            Exception? inner = null
        ) => new(
            $"Callback parameter type graph for '{function.exportName}:{parameter.sourceName ?? parameter.name}' is cyclic or incomplete at '{id.value}'. Safety analysis requires an intact type representation.", inner);
    }

    private static bool Explicit(Evidence? evidence) => evidence is not null
        && evidence.level is EvidenceLevel.Declared or EvidenceLevel.UserAsserted or EvidenceLevel.Verified
        && !string.IsNullOrWhiteSpace(evidence.origin);
}
