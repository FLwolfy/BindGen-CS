using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using BGCS.Contracts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Native;

namespace BGCS.Analysis.Safety;

/// <summary>Reports missing returned data-address and callback contracts without guessing ownership, encoding, lifetime or native invocation threads.</summary>
public sealed class SafetyAnalyzer
{
    /// <summary>Checks ownership, pointer validity and callback lifetime/thread evidence independently of raw calling ABI support.</summary>
    /// <param name="module">The immutable final native ABI and its declared semantic facts.</param>
    /// <returns>Ownership warnings for unproved data-pointer results and separate lifetime/thread warnings for unproved function-pointer parameters.</returns>
    /// <exception cref="ArgumentNullException">The module is null.</exception>
    /// <exception cref="InvalidOperationException">A returned or parameter type's alias, qualifier or pointer representation is cyclic or incomplete.</exception>
    public ImmutableArray<BindingDiagnostic> Analyze(InteropModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        ImmutableArray<BindingDiagnostic>.Builder diagnostics = ImmutableArray.CreateBuilder<BindingDiagnostic>();
        diagnostics.AddRange(CallbackSafetyAnalyzer.Analyze(module));
        foreach (InteropFunction function in module.functions)
        {
            if (!IsDataPointer(module, function)) continue;
            if (function.returnOwnership is null && ReturnedRegistrationSafety.IsComplete(module, function)) continue;
            List<string> missing = [];
            OwnershipContract? ownership = function.returnOwnership;
            if (ownership is null) missing.Add("return ownership and pointer lifetime are not declared");
            else
            {
                if (!Enum.IsDefined(ownership.kind) || ownership.kind == NativeOwnershipKind.Unknown)
                    missing.Add("return ownership is unknown");
                if (!Explicit(ownership.evidence)) missing.Add("ownership evidence is missing, inferred or lacks a source");
                LifetimeContract? lifetime = ownership.lifetime;
                if (lifetime is null) missing.Add("pointer lifetime is not declared");
                else
                {
                    if (!Enum.IsDefined(lifetime.kind) || lifetime.kind == NativeLifetimeKind.Unknown)
                        missing.Add("pointer lifetime is unknown");
                    if (!Explicit(lifetime.evidence)) missing.Add("lifetime evidence is missing, inferred or lacks a source");
                    if (lifetime.kind is NativeLifetimeKind.Owner or NativeLifetimeKind.Registration or NativeLifetimeKind.Operation
                        && (lifetime.owner is not StableId owner || string.IsNullOrWhiteSpace(owner.value)))
                        missing.Add("the declared lifetime does not identify its owner or terminal protocol");
                }
            }
            if (missing.Count == 0) continue;
            diagnostics.Add(new(BindingDiagnosticSeverity.Warning,
                $"Native data-pointer result '{function.exportName}' has incomplete safety facts: {string.Join("; ", missing)}. "
                + (ReturnedRegistrationSafety.HasIntent(function)
                    ? "Complete the semantics.callbackParameters returnedHandle registration and exact unregister protocol, including full native-frame quiescence and null-result cleanup; "
                        + "an async result also requires its matching semantics.asyncOperations completion and terminal join. Do not add a competing ownedResults cleanup to the same registration token. "
                    : "Declare semantics.ownedResults with its matching release contract, semantics.borrowedResults with an explicit Module address-validity contract, "
                        + "or supply explicit ownership and lifetime contracts through an interop plugin. ")
                + "Pointer spelling and callable names do not establish these facts.",
                BindingDiagnosticCodes.C_OWNERSHIP)
            { location = function.source });
        }
        return diagnostics.ToImmutable();
    }

    private static bool Explicit(Evidence? evidence) => evidence is not null
        && evidence.level is EvidenceLevel.Declared or EvidenceLevel.UserAsserted or EvidenceLevel.Verified
        && !string.IsNullOrWhiteSpace(evidence.origin);

    private static bool IsDataPointer(
        InteropModule module,
        InteropFunction function
    ) {
        HashSet<StableId> representation = [];
        InteropType type = Resolve(function.returnType);
        if (type.category != NativeTypeCategory.Pointer) return false;
        bool direct = true, callable = false;
        do
        {
            if (type.elementType is not StableId element) throw Invalid(type.id);
            type = Resolve(element);
            if (direct) callable = type.category == NativeTypeCategory.Function;
            direct = false;
        }
        while (type.category == NativeTypeCategory.Pointer);
        return !callable;

        InteropType Resolve(StableId id)
        {
            while (true)
            {
                if (string.IsNullOrWhiteSpace(id.value) || !representation.Add(id)) throw Invalid(id);
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
            $"Returned type graph for native export '{function.exportName}' is cyclic or incomplete at '{id.value}'. Ownership analysis requires an intact type representation.", inner);
    }
}
