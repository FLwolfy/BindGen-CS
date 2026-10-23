using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using BGCS.Core.Targets;
using BGCS.Core.Calls;
using BGCS.Analysis.Calls;

namespace BGCS.CSharp.Planning;

/// <summary>Checks aggregate field representation, managed alignment and calling classification independently.</summary>
internal sealed class AggregateAbiPlanner
{
    private readonly InteropModule module;
    private readonly ManagedRuntimeProfile runtime;
    private readonly Dictionary<(NativeCallDirection, StableId), NativeCallPlan> m_plans = [];

    internal AggregateAbiPlanner(
        InteropModule module,
        ManagedRuntimeProfile runtime,
        ImmutableArray<NativeCallPlan> plans
    ) {
        this.module = module;
        this.runtime = runtime;
        CallPlanValidator validation = new(module, runtime);
        foreach (NativeCallPlan plan in plans)
        {
            validation.Verify(plan);
            if (!m_plans.TryAdd((plan.signature.direction, plan.signature.id), plan))
                throw new InvalidOperationException("A physical signature has more than one direct-call plan.");
        }
    }

    private readonly Dictionary<StableId, Shape?> m_shapes = [];
    private readonly Dictionary<StableId, CSharpAggregateCallPlan> m_calls = [];

    internal ImmutableArray<CSharpAggregateCallPlan> calls => m_calls.Values.OrderBy(call => call.nativeType.value, StringComparer.Ordinal).ToImmutableArray();

    internal (int Packing, int? Alignment) Storage(StableId id)
    {
        Shape? shape = Describe(id, []);
        return shape is null ? (1, null) : (shape.Alignment, shape.Alignment);
    }

    internal void RequireSignature(NativeCallSignature signature)
    {
        if (!m_plans.TryGetValue((signature.direction, signature.id), out NativeCallPlan? plan)
            || !CallPlanValidator.SameSignature(signature, plan.signature))
            throw new NotSupportedException("The physical call, including any aggregate carriers, has no complete plan from its explicitly selected calling-ABI provider. Select the matching implementation or generate a native pointer bridge/shim for this signature.");
    }

    internal void CheckCall(StableId id)
    {
        InteropType type = Underlying(id);
        if (m_calls.ContainsKey(type.id)) return;
        Shape shape = Describe(type.id, [])
            ?? throw Unsupported(type, "Its complete managed storage cannot be represented independently of calling classification.");
        NativeCallPlan[] sites = m_plans.Values.Where(plan => plan.parameters.Any(value => Underlying(value.nativeType).id == type.id)
            || plan.returnValue is not null && Underlying(plan.returnValue.nativeType).id == type.id).ToArray();
        NativeCallValuePlan[] values = sites.SelectMany(plan => plan.returnValue is null ? plan.parameters : plan.parameters.Add(plan.returnValue))
            .Where(value => Underlying(value.nativeType).id == type.id).ToArray();
        if (values.Length == 0 || values.Any(value => value.size != shape.Size || value.alignment != shape.Alignment))
            throw Unsupported(type, "No exact ABI plan establishes this carrier's calling representation.");
        m_calls.Add(type.id, new(type.id, runtime.callAbi, shape.Size, shape.Alignment,
            sites.OrderBy(plan => plan.signature.direction).ThenBy(plan => plan.signature.id.value, StringComparer.Ordinal).ToImmutableArray()));
    }

    private Shape? Describe(
        StableId id,
        HashSet<StableId> active
    ) {
        InteropType type = Underlying(id);
        if (m_shapes.TryGetValue(type.id, out Shape? known)) return known;
        if (!active.Add(type.id)) return null;
        Shape? result = null;
        try
        {
            if (type.size is not > 0 || type.size > int.MaxValue || type.alignment is not (1 or 2 or 4 or 8)
                || type.size.Value % type.alignment.Value != 0) return null;
            int size = checked((int)type.size.Value), alignment = type.alignment.Value;
            if (type.category is NativeTypeCategory.Pointer or NativeTypeCategory.Function)
                result = size == module.pointerSize && alignment == module.pointerSize ? new(size, alignment) : null;
            else if (type.category == NativeTypeCategory.Enumeration && type.elementType is StableId integral)
            {
                Shape? underlying = Describe(integral, active);
                result = underlying is not null && underlying.Size == size && underlying.Alignment == alignment ? underlying : null;
            }
            else if (type.category == NativeTypeCategory.Primitive)
            {
                bool scalar = type.primitiveKind switch
                {
                    "Float" => size == 4,
                    "Double" => size == 8,
                    "Bool" => size == 1,
                    "Void" or "Float16" or "BFloat16" or "Half" or "LongDouble" => false,
                    _ => type.isSigned is not null && size is 1 or 2 or 4 or 8
                };
                result = scalar && alignment == Math.Min(size, 8) ? new(size, alignment) : null;
            }
            else if (type.category == NativeTypeCategory.Array && type.arrayLength is > 0 && type.elementType is StableId element)
            {
                Shape? item = Describe(element, active);
                result = item is not null && type.arrayLength <= int.MaxValue && (long)item.Size * type.arrayLength == size
                    && item.Alignment == alignment ? new(size, alignment) : null;
            }
            else if (type.category == NativeTypeCategory.Record && type.recordKind is "Struct" or "Union" && !type.fields.IsDefaultOrEmpty)
            {
                int naturalAlignment = 1;
                long previousEnd = 0;
                foreach (InteropField field in type.fields.OrderBy(field => field.offset))
                {
                    if (field.bitWidth is not null || field.offset is not >= 0) return null;
                    Shape? item = Describe(field.type, active);
                    if (item is null || field.offset.Value > size - item.Size) return null;
                    if (type.recordKind == "Union") { if (field.offset != 0) return null; }
                    else if (field.offset.Value < previousEnd) return null;
                    previousEnd = field.offset.Value + item.Size;
                    naturalAlignment = Math.Max(naturalAlignment, item.Alignment);
                }
                // Packing can decrease alignment. A managed fieldless alignment token would change the calling classification.
                result = alignment <= naturalAlignment ? new(size, alignment) : null;
            }
            return result;
        }
        finally
        {
            active.Remove(type.id);
            m_shapes[type.id] = result;
        }
    }

    private InteropType Underlying(StableId id)
    {
        HashSet<StableId> seen = [];
        InteropType type = module.GetType(id);
        while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
        {
            if (!seen.Add(type.id) || type.elementType is not StableId element)
                throw new NotSupportedException("An aggregate field has a cyclic or incomplete alias shape.");
            type = module.GetType(element);
        }
        return type;
    }

    private static NotSupportedException Unsupported(
        InteropType type,
        string reason
    )
        => new($"By-value use of '{type.nativeName}' requires independently verified managed calling-ABI evidence or a pointer bridge. {reason}");

    private sealed record Shape(
        int Size,
        int Alignment
    );
}
