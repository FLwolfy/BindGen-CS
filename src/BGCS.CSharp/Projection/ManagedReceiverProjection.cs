using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Analysis.Safety;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Managed;
using BGCS.Contracts.Native;

namespace BGCS.CSharp.Projection;

internal static class ManagedReceiverProjection
{
    internal static ManagedApiModel Apply(
        ManagedApiModel model,
        ImmutableArray<ManagedReceiverRequest> requests,
        Func<string, InteropFunction> resolveFunction
    ) {
        if (requests.IsDefault) throw new ArgumentException("C receiver/member mappings must be initialized.");
        var methods = model.methods;
        HashSet<StableId> selected = [];
        Dictionary<StableId, (string Name, ManagedReceiverKind Kind)> objects = [];
        foreach (ManagedReceiverRequest request in requests)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.nativeType);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.receiver);
            if (!Enum.IsDefined(request.kind)) throw new ArgumentException("The C receiver lifetime protocol is unknown.");
            if (request.kind == ManagedReceiverKind.ModuleBorrowed && request.releaseDomain is not null)
                throw new ArgumentException("A module-borrowed object has no native allocation/release domain.");
            if (request.kind == ManagedReceiverKind.Value && (request.objectName is not null || request.releaseDomain is not null))
                throw new ArgumentException("A value receiver uses its existing carrier and cannot declare an object wrapper name or release domain.");
            InteropFunction function = resolveFunction(request.function);
            if (!selected.Add(function.id) || function.sourceOwnerType is not null) throw new ArgumentException("An explicit C receiver must select one free native function exactly once.");
            InteropType[] matches = model.interop.types.Where(type => type.id.value == request.nativeType || type.nativeName == request.nativeType
                || type.declarationName == request.nativeType || type.sourceQualifiedName == request.nativeType).ToArray();
            InteropType[] records = matches.Where(type => type.category == NativeTypeCategory.Record).ToArray();
            if (records.Length == 1) matches = records;
            else matches = matches.Where(type => type.category == NativeTypeCategory.Alias).ToArray();
            if (matches.Length != 1) throw new ArgumentException("A C receiver must select one native record or named opaque-pointer alias: " + request.nativeType);
            InteropType native = matches[0];
            InteropType canonical = native;
            while (canonical.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified) canonical = model.interop.GetType(canonical.elementType!.Value);
            if (canonical.category == NativeTypeCategory.Record) native = canonical;
            if (request.kind == ManagedReceiverKind.Value && canonical.category != NativeTypeCategory.Record)
                throw new ArgumentException("A value receiver must select an exact native record, not a pointer alias.");
            InteropParameter[] receivers = function.parameters.Where(parameter => parameter.id.value == request.receiver || parameter.sourceName == request.receiver || parameter.name == request.receiver).ToArray();
            if (receivers.Length != 1 || !NativeReceiverTypeMatcher.Matches(model.interop, native.id, receivers[0].type))
                throw new ArgumentException("A C receiver parameter does not preserve the selected exact native record or opaque alias identity.");
            string name = request.objectName ?? model.types.Single(type => type.nativeType == native.id).name
                + (request.kind == ManagedReceiverKind.Value ? string.Empty : "Object");
            name = Planning.CSharpNames.Identifier(name);
            if (request.kind != ManagedReceiverKind.Value)
            {
                if (objects.TryGetValue(native.id, out var previous) && (previous.Name != name || previous.Kind != request.kind))
                    throw new ArgumentException("One native receiver identity has conflicting object wrapper names or lifetime protocols.");
                objects[native.id] = (name, request.kind);
            }
            ManagedMember method = methods.Single(method => method.nativeExport == function.id);
            methods = methods.SetItem(methods.IndexOf(method), method with
            {
                name = request.name ?? method.name,
                receiver = new(native.id, receivers[0].id, name)
                { kind = request.kind, requiredReleaseDomain = request.releaseDomain is null ? null : new("configured-release-domain:" + request.releaseDomain) }
            });
        }
        return model with { methods = methods };
    }
}
