using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;

namespace BGCS.Analysis.Bridge;

/// <summary>Rebinds source semantic relationships to the exact independently checked C bridge identities.</summary>
internal sealed class CBridgeSemanticRemapper(
    NativeModule source,
    ImmutableArray<InteropType> types,
    ImmutableArray<InteropFunction> functions,
    IReadOnlyDictionary<StableId, StableId> checkedTypes
)
{
    private readonly HashSet<StableId> m_parameters = source.declarations.Where(declaration => declaration.kind == "Parameter").Select(declaration => declaration.id).ToHashSet();
    private readonly HashSet<StableId> m_functions = source.declarations.Where(declaration => declaration.kind == "Function").Select(declaration => declaration.id).ToHashSet();

    internal ImmutableArray<InteropFunction> Apply() => functions.Select(Rebind).ToImmutableArray();

    private InteropFunction Rebind(InteropFunction current)
    {
        return current with
        {
            returnOwnership = Ownership(current.returnOwnership),
            returnString = Text(current.returnString),
            error = current.error is not { } error ? null : error with
            {
                valueParameter = Parameter(error.valueParameter),
                errorParameter = Parameter(error.errorParameter),
                errorOwnership = Ownership(error.errorOwnership),
                errorText = Text(error.errorText)
            },
            asynchronous = current.asynchronous is not { } operation ? null : operation with
            {
                completionCallback = Parameter(operation.completionCallback)!.Value,
                retainedParameters = operation.retainedParameters.Select(id => Parameter(id)!.Value).ToImmutableArray(),
                cancelFunction = Export(operation.cancelFunction),
                cancellationHandleParameter = Parameter(operation.cancellationHandleParameter)
            },
            parameters = current.parameters.Select(parameter => parameter with
            {
                ownership = Ownership(parameter.ownership),
                text = Text(parameter.text),
                buffer = parameter.buffer is not { } buffer ? null : buffer with
                {
                    elementType = Storage(buffer.elementType),
                    lifetime = Lifetime(buffer.lifetime),
                    lengthParameter = Parameter(buffer.lengthParameter),
                    capacityParameter = Parameter(buffer.capacityParameter),
                    writtenCountParameter = Parameter(buffer.writtenCountParameter)
                },
                singleValue = parameter.singleValue is not { } value ? null : value with { lifetime = Lifetime(value.lifetime) },
                callback = parameter.callback is not { } callback ? null : callback with
                {
                    lifetime = Lifetime(callback.lifetime),
                    userDataParameter = Parameter(callback.userDataParameter),
                    unregisterFunction = Export(callback.unregisterFunction),
                    registration = callback.registration is not { } registration ? null : registration with
                    {
                        unregisterHandleParameter = Parameter(registration.unregisterHandleParameter),
                        unregisterCallbackParameter = Parameter(registration.unregisterCallbackParameter),
                        unregisterUserDataParameter = Parameter(registration.unregisterUserDataParameter)
                    }
                }
            }).ToImmutableArray()
        };

        StableId? Parameter(StableId? identity)
        {
            if (identity is not StableId id) return null;
            StableId[] local = current.parameters.Where(parameter => parameter.sourceDeclaration == id).Select(parameter => parameter.id).Distinct().ToArray();
            if (local.Length == 1) return local[0];
            if (local.Length > 1) throw Ambiguous("parameter", id);
            if (current.parameters.Any(parameter => parameter.id == id)) return id;
            StableId[] matches = functions.SelectMany(function => function.parameters).Where(parameter => parameter.sourceDeclaration == id).Select(parameter => parameter.id).Distinct().ToArray();
            return matches.Length == 1 ? matches[0] : throw Ambiguous("parameter", id);
        }
        StableId? Export(StableId? identity)
        {
            if (identity is not StableId id) return null;
            StableId[] matches = functions.Where(function => function.sourceDeclaration == id).Select(function => function.id).Distinct().ToArray();
            if (matches.Length == 1) return matches[0];
            if (matches.Length == 0 && functions.Any(function => function.id == id)) return id;
            throw Ambiguous("export", id);
        }
        LifetimeContract Lifetime(LifetimeContract lifetime) => lifetime with
        {
            owner = lifetime.owner is not StableId owner ? null : m_parameters.Contains(owner) ? Parameter(owner) : m_functions.Contains(owner) ? Export(owner) : owner
        };
        OwnershipContract? Ownership(OwnershipContract? ownership) => ownership is null ? null : ownership with
        { lifetime = Lifetime(ownership.lifetime), releaseFunction = Export(ownership.releaseFunction) };
        StringContract? Text(StringContract? text) => text is null ? null : text with
        { lifetime = Lifetime(text.lifetime), lengthParameter = Parameter(text.lengthParameter), ownership = Ownership(text.ownership) };
        StableId Storage(StableId identity)
        {
            NativeType original = StandardLibraryFactReader.Resolve(source, identity);
            if (checkedTypes.TryGetValue(original.id, out StableId mapped) && types.Any(type => type.id == mapped)) return mapped;
            throw new NotSupportedException("A C bridge semantic storage relationship has no exact checked physical type: " + identity.value + ". Use an explicit callable lowering.");
        }
        NotSupportedException Ambiguous(
            string kind,
            StableId identity
        ) => new("The C bridge cannot bind " + kind + " relationship '" + identity.value
            + "' to exactly one physical channel for '" + current.exportName + "'. Include its exact declaration or provide an explicit joint callable lowering for finite variants.");
    }
}
