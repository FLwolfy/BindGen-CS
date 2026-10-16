using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Analysis.Safety;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Managed;
using BGCS.Contracts.Native;
using BGCS.Contracts.Requests;

namespace BGCS.CSharp.Projection;

internal static class ManagedPointerProjection
{
    internal static ManagedApiModel Apply(
        ManagedApiModel model,
        ManagedProjectionRequest request,
        Func<string, InteropFunction> resolve
    ) {
        if (request.pointerCarriers.IsDefault || request.pointerFunctions.IsDefault) throw new ArgumentException("Pointer presentation collections must be initialized.");
        NativePointerShapeReader reader = new(model.interop);
        var carriers = ImmutableArray.CreateBuilder<ManagedPointerCarrier>();
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (ManagedPointerCarrierRequest item in request.pointerCarriers)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentException.ThrowIfNullOrWhiteSpace(item.nativeType);
            string name = Planning.CSharpNames.Identifier(item.name);
            if (item.pointerDepth <= 0 || !names.Add(name.TrimStart('@'))) throw new ArgumentException("Pointer carriers require positive depth and distinct public names.");
            InteropType[] selected = model.interop.types.Where(type => type.id.value == item.nativeType).ToArray();
            if (selected.Length == 0) selected = model.interop.types.Where(type => type.nativeName == item.nativeType || type.declarationName == item.nativeType || type.sourceQualifiedName == item.nativeType).ToArray();
            NativePointerShape[] direct = selected.Select(type => reader.Read(type.id)).OfType<NativePointerShape>()
                .Where(shape => shape.depth == item.pointerDepth && shape.readOnly == item.readOnly).ToArray();
            NativePointerShape chosen;
            if (direct.Length == 1) chosen = direct[0];
            else
            {
                InteropType[] records = selected.Select(Canonical).Where(type => type.category == NativeTypeCategory.Record).DistinctBy(type => type.id).ToArray();
                if (direct.Length != 0 || records.Length != 1) throw new ArgumentException("Select one exact record or pointer identity with matching depth and const qualification: " + item.nativeType);
                NativePointerShape[][] candidates = model.interop.types.Where(type => type.category == NativeTypeCategory.Pointer)
                    .Select(type => reader.Read(type.id)).OfType<NativePointerShape>()
                    .Where(shape => shape.record == records[0].id && shape.depth == item.pointerDepth && shape.readOnly == item.readOnly)
                    .GroupBy(shape => string.Join(";", shape.qualifiers.Select(level => string.Join(",", level))) + "|" + string.Join(";", shape.pointerAliases), StringComparer.Ordinal)
                    .Select(group => group.OrderBy(shape => shape.pointer.value, StringComparer.Ordinal).ToArray()).ToArray();
                if (candidates.Length != 1) throw new ArgumentException("The requested native pointer graph is missing or ambiguous; select its exact inspected pointer identity: " + item.nativeType);
                chosen = candidates[0][0];
            }
            carriers.Add(new(chosen.pointer, name) { documentation = item.documentation });
        }
        var functions = ImmutableArray.CreateBuilder<ManagedPointerFunction>();
        HashSet<StableId> exports = [];
        foreach (ManagedPointerFunctionRequest item in request.pointerFunctions)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (item.parameters.IsDefault || item.parameters.Length == 0 && item.returns is null) throw new ArgumentException("An address function must select at least one pointer parameter or result.");
            InteropFunction function = resolve(item.function);
            if (!exports.Add(function.id)) throw new ArgumentException("A native function has duplicate pointer-address presentation.");
            HashSet<StableId> parameters = [];
            ImmutableArray<ManagedPointerParameter> mapped = item.parameters.Select(parameter =>
            {
                ArgumentNullException.ThrowIfNull(parameter);
                InteropParameter[] match = function.parameters.Where(candidate => candidate.id.value == parameter.parameter || candidate.name == parameter.parameter || candidate.sourceName == parameter.parameter).ToArray();
                if (match.Length != 1 || !parameters.Add(match[0].id)) throw new ArgumentException("A pointer parameter must select one exact native identity once.");
                CheckName(parameter.carrier);
                return new ManagedPointerParameter(match[0].id, Planning.CSharpNames.Identifier(parameter.carrier));
            }).ToImmutableArray();
            if (item.returns is not null) CheckName(item.returns);
            StableId? receiver = null;
            if (item.receiver is not null)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(item.receiver);
                InteropParameter[] match = function.parameters.Where(candidate => candidate.id.value == item.receiver || candidate.name == item.receiver || candidate.sourceName == item.receiver).ToArray();
                if (match.Length != 1 || !mapped.Any(parameter => parameter.nativeParameter == match[0].id))
                    throw new ArgumentException("An address receiver must select exactly one explicitly mapped address parameter.");
                receiver = match[0].id;
            }
            functions.Add(new(function.id, mapped) { name = item.name, returns = item.returns is null ? null : Planning.CSharpNames.Identifier(item.returns), receiver = receiver });
        }
        return model with { pointerCarriers = carriers.ToImmutable(), pointerFunctions = functions.ToImmutable() };

        void CheckName(string name)
        {
            if (!carriers.Any(carrier => carrier.name == Planning.CSharpNames.Identifier(name))) throw new ArgumentException("A pointer function references an undeclared address carrier: " + name);
        }
        InteropType Canonical(InteropType type)
        {
            HashSet<StableId> active = [];
            while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
            {
                if (!active.Add(type.id) || type.elementType is not StableId element) throw new ArgumentException("The selected pointer alias is cyclic or incomplete.");
                type = model.interop.GetType(element);
            }
            return type;
        }
    }
}
