using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Analysis.Safety;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;
using BGCS.CSharp.Emission;
using BGCS.CSharp.Validation;

namespace BGCS.CSharp.Planning;

internal static class PointerCarrierPlanner
{
    private static bool RequiredMember(string name) => name is "m_handle" or "Handle" or "Null" or "IsNull" or "CheckAddress"
        or "DangerousGetPointer" or "DangerousGetReference" or "DangerousGetElement" or "DangerousGetAddressElement"
        or "DangerousSetAddressElement" or "Equals" or "GetHashCode";

    internal static CSharpEmissionPlan Apply(CSharpEmissionPlan plan)
    {
        if (plan.api.pointerCarriers.IsDefault || plan.api.pointerFunctions.IsDefault) throw new ArgumentException("Pointer presentation collections must be initialized.");
        if (plan.api.pointerCarriers.IsEmpty && plan.api.pointerFunctions.IsEmpty) return plan;
        NativePointerShapeReader reader = new(plan.api.interop);
        Dictionary<StableId, InteropType> nativeTypes = plan.api.interop.types.ToDictionary(type => type.id);
        Dictionary<StableId, CSharpTypePlan> types = plan.types.ToDictionary(type => type.nativeType);
        Dictionary<StableId, CSharpStoragePlan> storage = plan.storage.ToDictionary(type => type.nativeType);
        HashSet<string> names = plan.storage.Select(type => type.name.TrimStart('@')).Concat(plan.managedEnums.Select(type => type.name.TrimStart('@')))
            .Concat(plan.abiDelegates.Select(type => type.name.TrimStart('@'))).Append(plan.api.name.TrimStart('@')).Append(plan.contextName.TrimStart('@')).ToHashSet(StringComparer.Ordinal);
        var carriers = ImmutableArray.CreateBuilder<CSharpPointerCarrierPlan>();
        HashSet<StableId> identities = [];
        foreach (ManagedPointerCarrier item in plan.api.pointerCarriers)
        {
            ArgumentNullException.ThrowIfNull(item);
            string name = CSharpNames.Identifier(item.name);
            if (!identities.Add(item.nativePointer)) throw new ArgumentException("One exact pointer identity cannot have multiple address-carrier presentations.");
            if (!names.Add(name.TrimStart('@')) || RequiredMember(name.TrimStart('@')))
                throw new ArgumentException("A pointer address-carrier name conflicts with a generated type or required member.");
            NativePointerShape shape = reader.Read(item.nativePointer) ?? throw new CSharpPlanningException(item.nativePointer, nativeTypes[item.nativePointer].source,
                "An address carrier requires an exact complete data-pointer graph ending at a native record or opaque void address.");
            string pointerType = types[item.nativePointer].syntax;
            InteropType immediate = Canonical(item.nativePointer);
            StableId elementId = immediate.elementType!.Value;
            InteropType element = Canonical(elementId);
            bool dereference = !shape.hasVolatilePointee && element.size is > 0 && element.size <= int.MaxValue
                && element.alignment is > 0 && (element.alignment.Value & (element.alignment.Value - 1)) == 0
                && !shape.qualifiers.Skip(2).Any(level => level.Contains("Const", StringComparer.Ordinal));
            bool constStorage = ContainsConstStorage(elementId, []);
            bool finalConstStorage = ContainsConstStorage(shape.record, []);
            bool fieldViews = dereference;
            bool volatileStorage = ContainsVolatileStorage(elementId, []);
            bool restrictedPointerStorage = RestrictedPointerStorage(elementId, [], false);
            if (shape.depth > 1 && finalConstStorage) dereference = false;
            if (volatileStorage || restrictedPointerStorage) dereference = false;
            ImmutableArray<CSharpPointerFieldPlan>.Builder fields = ImmutableArray.CreateBuilder<CSharpPointerFieldPlan>();
            if (shape.depth == 1 && fieldViews && storage.TryGetValue(shape.record, out CSharpStoragePlan? record))
            {
                InteropType native = nativeTypes[shape.record];
                foreach (CSharpFieldPlan field in record.fields)
                {
                    InteropField original = native.fields.Single(candidate => candidate.id == field.nativeField);
                    if (ContainsVolatileStorage(original.type, []) || ValueLikeRestricted(original.type)) continue;
                    InteropType value = Canonical(original.type);
                    bool readOnly = shape.readOnly || ContainsConstStorage(original.type, []);
                    if (value.category == NativeTypeCategory.Array && value.arrayLength is > 0 && value.arrayLength <= int.MaxValue && value.elementType is StableId arrayElement)
                    {
                        InteropType leaf = Canonical(arrayElement);
                        if (leaf.category is not (NativeTypeCategory.Pointer or NativeTypeCategory.Function) && leaf.size is > 0)
                            fields.Add(new(field.name, types[arrayElement].syntax, field.offset, readOnly, original.type)
                            { arrayLength = checked((int)value.arrayLength.Value), documentation = field.documentation });
                        else fields.Add(new(field.name, field.type, field.offset, readOnly, original.type) { documentation = field.documentation });
                    }
                    else fields.Add(new(field.name, field.type, field.offset, readOnly, original.type) { pointer = value.category is NativeTypeCategory.Pointer or NativeTypeCategory.Function, documentation = field.documentation });
                }
                foreach (CSharpBitfieldPlan bitfield in record.bitfields)
                    fields.Add(new(bitfield.name, bitfield.type, 0, shape.readOnly, shape.record) { bitfield = true, documentation = bitfield.documentation });
            }
            if (name.TrimStart('@') is "DangerousGetAddressElement" or "DangerousSetAddressElement"
                || fields.Any(field => (field.arrayLength is null ? "DangerousField_" : "DangerousArray_") + field.name.TrimStart('@') == name.TrimStart('@')))
                throw new ArgumentException("A pointer carrier name conflicts with its required unsafe view member.");
            carriers.Add(new(item.nativePointer, name, pointerType, types[elementId].syntax, shape.depth, shape.readOnly,
                dereference ? checked((int?)element.size) : null, element.alignment, plan.api.interop.pointerSize, fields.ToImmutable())
            {
                elementReadOnly = shape.readOnly || constStorage,
                exposesTypedPointer = !shape.hasVolatilePointee && !volatileStorage && !restrictedPointerStorage && !constStorage && !finalConstStorage && !shape.qualifiers.Skip(1).Any(level => level.Contains("Const", StringComparer.Ordinal)),
                documentation = item.documentation ?? nativeTypes[item.nativePointer].documentation ?? nativeTypes[shape.record].documentation,
                elementCarrier = !shape.hasVolatilePointee && shape.depth > 1 ? carriers.FirstOrDefault(candidate => candidate.nativePointer == elementId)?.name : null
            });
        }
        // Pointer fields may refer to a carrier declared later; resolve after every carrier identity is frozen.
        ImmutableArray<CSharpPointerCarrierPlan> resolved = carriers.Select(carrier => carrier with
        {
            elementCarrier = reader.Read(carrier.nativePointer) is { depth: > 1, hasVolatilePointee: false }
                ? FindCarrier(Canonical(carrier.nativePointer).elementType!.Value) : null,
            fields = carrier.fields.Select(field => field.pointer ? field with
            {
                carrier = FindCarrier(field.nativeType)
            } : field).ToImmutableArray()
        }).ToImmutableArray();
        Dictionary<string, CSharpPointerCarrierPlan> byName = resolved.ToDictionary(carrier => carrier.name, StringComparer.Ordinal);
        var functions = ImmutableArray.CreateBuilder<CSharpPointerFunctionPlan>();
        HashSet<StableId> seen = [];
        foreach (ManagedPointerFunction item in plan.api.pointerFunctions)
        {
            ArgumentNullException.ThrowIfNull(item);
            int index = Array.FindIndex(plan.methods.ToArray(), method => method.nativeExport == item.nativeExport);
            if (index < 0 || !seen.Add(item.nativeExport) || item.parameters.IsDefault || item.parameters.Length == 0 && item.returns is null)
                throw new ArgumentException("An address function must select one existing import with explicit address channels.");
            CSharpMethodPlan method = plan.methods[index];
            InteropFunction function = plan.api.interop.functions.Single(candidate => candidate.id == item.nativeExport);
            if (function.error is not null || function.asynchronous is not null || function.parameters.Any(parameter => parameter.callback is not null))
                throw Failure("Address presentation cannot bypass checked status, async or callback lifetime operations.");
            Dictionary<int, string> inputs = [];
            foreach (ManagedPointerParameter parameter in item.parameters)
            {
                int at = Array.FindIndex(function.parameters.ToArray(), candidate => candidate.id == parameter.nativeParameter);
                if (at < 0 || !byName.TryGetValue(CSharpNames.Identifier(parameter.carrier), out CSharpPointerCarrierPlan? carrier) || !inputs.TryAdd(at, carrier.name))
                    throw Failure("A pointer input lost its exact native parameter or selected carrier.");
                InteropParameter native = function.parameters[at];
                if (!reader.CanConvert(carrier.nativePointer, native.type)) throw Failure("A pointer input would change its exact alias or remove native pointee const qualification.");
                if (native.text is not null || native.buffer is not null || native.singleValue is not null || native.ownership is { kind: not NativeOwnershipKind.Borrowed }
                    || method.valueProjections.ContainsKey(at)) throw Failure("An address input cannot replace a competing text, buffer, temporary-value or ownership-transfer protocol.");
            }
            if (Enumerable.Range(0, function.parameters.Length).Where(at => !inputs.ContainsKey(at)).Any(at => !OverloadPlanner.SupportsDirectInput(plan, method, function, at)))
                throw Failure("Every other input requires an existing checked direct scalar, text, buffer or value protocol.");
            CSharpPointerCarrierPlan? returned = item.returns is null ? null : byName.GetValueOrDefault(CSharpNames.Identifier(item.returns));
            if (item.returns is not null && (returned is null || !reader.CanConvert(function.returnType, returned.nativePointer)))
                throw Failure("A pointer result would change its exact alias or remove native pointee const qualification.");
            if (function.returnOwnership is not null || function.returnString is not null)
                throw Failure("A tracked owned/module-borrowed/text result must keep its checked factory or lifetime projection; use its scoped Invoke for explicit address access.");
            if (returned is null && !OverloadPlanner.SupportsDirectResult(plan, function.returnType)) throw Failure("A pointer result requires an explicitly selected non-owning address carrier.");
            int[] visible = OverloadPlanner.PublicInputs(method, Enumerable.Range(0, method.parameterTypes.Length)).ToArray();
            CSharpFacadeMethodPlan signature = new(CSharpNames.Identifier(item.name ?? method.name), returned is null ? RecipeConversionEmitter.DirectResultType(method)
                    : CSharpGeneratedTypeScope.QualifiedReference(plan.api, returned.name),
                visible.Select(at => inputs.TryGetValue(at, out string? carrier) ? CSharpGeneratedTypeScope.QualifiedReference(plan.api, carrier) : OverloadPlanner.InputType(method, at)).ToImmutableArray(),
                visible.Select(at => method.parameterNames[at]).ToImmutableArray())
            {
                nativeExport = function.id,
                inputIndices = visible.ToImmutableArray(),
                parameterModifiers = visible.Select(at => inputs.ContainsKey(at) ? string.Empty : OverloadPlanner.InputModifier(method, at)).ToImmutableArray(),
                documentation = method.documentation,
                documentationContent = method.documentationContent is { } content ? content with { parameters = visible.Select(at => content.parameters[at]).ToImmutableArray() } : null
            };
            int? receiverIndex = null;
            string? receiverCarrier = null;
            if (item.receiver is StableId receiver)
            {
                int position = Array.FindIndex(function.parameters.ToArray(), parameter => parameter.id == receiver);
                if (position < 0 || !inputs.TryGetValue(position, out receiverCarrier) || !visible.Contains(position))
                    throw Failure("An address receiver must retain one exact visible address parameter.");
                CSharpPointerCarrierPlan carrier = byName[receiverCarrier];
                string member = signature.name.TrimStart('@');
                if (member == carrier.name.TrimStart('@') || RequiredMember(member)
                    || carrier.fields.Any(field => member == (field.arrayLength is null ? "DangerousField_" : "DangerousArray_") + field.name.TrimStart('@')))
                    throw Failure("An address receiver method conflicts with its carrier's required storage or unsafe view member.");
                receiverIndex = position;
            }
            functions.Add(new(index, signature, inputs.ToImmutableDictionary(), returned?.name)
            { receiverIndex = receiverIndex, receiverCarrier = receiverCarrier });
            CSharpPlanningException Failure(string message) => new(function.id, function.source, message);
        }
        return plan with { pointerCarriers = resolved, pointerFunctions = functions.ToImmutable() };

        string? FindCarrier(StableId identity)
        {
            CSharpPointerCarrierPlan? exact = carriers.SingleOrDefault(candidate => candidate.nativePointer == identity);
            if (exact is not null) return exact.name;
            CSharpPointerCarrierPlan[] equivalent = carriers.Where(candidate => reader.CanConvert(identity, candidate.nativePointer)
                && reader.CanConvert(candidate.nativePointer, identity)).ToArray();
            if (equivalent.Length > 1) throw new ArgumentException("A pointer field has multiple equivalent address presentations; select unique exact pointer identities.");
            return equivalent.SingleOrDefault()?.name;
        }

        InteropType Canonical(StableId identity)
        {
            InteropType type = nativeTypes[identity];
            HashSet<StableId> visited = [];
            while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
            {
                if (!visited.Add(type.id) || type.elementType is not StableId next) throw new InvalidOperationException("A pointer field alias is cyclic or incomplete.");
                type = nativeTypes[next];
            }
            return type;
        }
        bool HasQualifier(
            StableId identity,
            string qualifier
        ) {
            InteropType type = nativeTypes[identity];
            HashSet<StableId> visited = [];
            while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
            {
                if (!visited.Add(type.id) || type.elementType is not StableId next) throw new InvalidOperationException("A pointer field alias is cyclic or incomplete.");
                if (type.qualifier == qualifier) return true;
                type = nativeTypes[next];
            }
            return type.qualifier == qualifier;
        }
        bool ContainsConstStorage(
            StableId identity,
            HashSet<StableId> visited
        ) {
            if (!visited.Add(identity)) return false;
            if (HasQualifier(identity, "Const") || HasQualifier(identity, "Volatile")) return true;
            InteropType type = Canonical(identity);
            if (type.category == NativeTypeCategory.Pointer) return false;
            return type.category == NativeTypeCategory.Array && type.elementType is StableId element ? ContainsConstStorage(element, visited)
                : type.category == NativeTypeCategory.Record && type.fields.Any(field => ContainsConstStorage(field.type, visited));
        }
        bool ContainsVolatileStorage(
            StableId identity,
            HashSet<StableId> visited
        ) {
            if (!visited.Add(identity)) return false;
            if (HasQualifier(identity, "Volatile")) return true;
            InteropType type = Canonical(identity);
            return type.category is NativeTypeCategory.Pointer or NativeTypeCategory.Array && type.elementType is StableId element
                ? ContainsVolatileStorage(element, visited)
                : type.category == NativeTypeCategory.Record && type.fields.Any(field => ContainsVolatileStorage(field.type, visited));
        }
        bool ValueLikeRestricted(StableId identity) => Canonical(identity).category is not (NativeTypeCategory.Pointer or NativeTypeCategory.Function)
            && RestrictedPointerStorage(identity, [], false);
        bool RestrictedPointerStorage(
            StableId identity,
            HashSet<(StableId, bool)> visited,
            bool throughPointer
        ) {
            if (!visited.Add((identity, throughPointer))) return false;
            if (throughPointer && HasQualifier(identity, "Const")) return true;
            InteropType type = Canonical(identity);
            if (type.category is NativeTypeCategory.Pointer or NativeTypeCategory.Array && type.elementType is StableId element)
                return RestrictedPointerStorage(element, visited, throughPointer || type.category == NativeTypeCategory.Pointer);
            return type.category == NativeTypeCategory.Record && type.fields.Any(field => RestrictedPointerStorage(field.type, visited, throughPointer));
        }
    }
}

/// <summary>Plans a non-owning native address; checked unsafe views never establish lifetime or allocation bounds.</summary>
/// <param name="nativePointer">The unchanged native pointer identity.</param>
/// <param name="name">The public address type.</param>
/// <param name="pointerType">The exact raw ABI pointer syntax.</param>
/// <param name="elementType">The first native pointee's raw syntax.</param>
/// <param name="depth">The exact indirection count.</param>
/// <param name="readOnly">Whether the first native pointee is const.</param>
/// <param name="elementSize">A supported complete dereference stride, or null.</param>
/// <param name="elementAlignment">The native element alignment when known.</param>
/// <param name="pointerSize">The target native pointer width.</param>
/// <param name="fields">Exact raw storage field views.</param>
public sealed record CSharpPointerCarrierPlan(
    StableId nativePointer,
    string name,
    string pointerType,
    string elementType,
    int depth,
    bool readOnly,
    int? elementSize,
    int? elementAlignment,
    int pointerSize,
    ImmutableArray<CSharpPointerFieldPlan> fields
)
{
    /// <summary>Gets whether a complete element view must preserve const storage transitively.</summary>
    public bool elementReadOnly { get; init; }
    /// <summary>Gets whether a typed writable pointer would preserve all known pointee qualifiers.</summary>
    public bool exposesTypedPointer { get; init; }
    /// <summary>Gets an exact lower-indirection address carrier for pointer-slot access without wrapper ref-punning.</summary>
    public string? elementCarrier { get; init; }
    /// <summary>Gets inert user or source prose, escaped as documentation rather than interpreted as C# or XML commands.</summary>
    public string? documentation { get; init; }
}

/// <summary>Plans one exact native field view under explicit caller-controlled address validity.</summary>
/// <param name="name">The original planned field name.</param>
/// <param name="type">The exact raw field or array-element syntax.</param>
/// <param name="offset">The verified byte offset.</param>
/// <param name="readOnly">Whether writes would remove native const.</param>
/// <param name="nativeType">The original native field identity.</param>
public sealed record CSharpPointerFieldPlan(
    string name,
    string type,
    int offset,
    bool readOnly,
    StableId nativeType
)
{
    /// <summary>Gets whether this field stores a native pointer.</summary>
    public bool pointer { get; init; }
    /// <summary>Gets an exactly matched pointer field carrier, or null to retain its raw pointer value.</summary>
    public string? carrier { get; init; }
    /// <summary>Gets the proven fixed native array extent.</summary>
    public int? arrayLength { get; init; }
    /// <summary>Gets whether the existing raw carrier supplies a checked bitfield property.</summary>
    public bool bitfield { get; init; }
    /// <summary>Gets inert original field documentation.</summary>
    public string? documentation { get; init; }
}

/// <summary>Plans a finite address-based function without changing any native signature or ownership evidence.</summary>
/// <param name="methodIndex">The existing checked import index.</param>
/// <param name="signature">The explicit managed forwarding surface.</param>
/// <param name="parameters">Native parameter positions and selected address carriers.</param>
/// <param name="returnCarrier">The untracked address result carrier, or null.</param>
public sealed record CSharpPointerFunctionPlan(
    int methodIndex,
    CSharpFacadeMethodPlan signature,
    ImmutableDictionary<int, string> parameters,
    string? returnCarrier
)
{
    /// <summary>Gets the exact native parameter position represented by this address receiver.</summary>
    public int? receiverIndex { get; init; }
    /// <summary>Gets the selected non-owning carrier whose member requires an explicit API context.</summary>
    public string? receiverCarrier { get; init; }
}
