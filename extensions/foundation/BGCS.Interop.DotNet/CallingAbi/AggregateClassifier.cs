using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using BGCS.Core.Targets;
using BGCS.Core.Calls;

namespace BGCS.Interop.DotNet.CallingAbi;

/// <summary>Checks aggregate field representation, managed alignment and calling classification independently.</summary>
internal sealed class AggregateClassifier(
    InteropModule module,
    string family
)
{
    private readonly Dictionary<StableId, Shape?> m_shapes = [];
    private readonly Dictionary<StableId, NativeCallValuePlan> m_calls = [];

    internal NativeCallValuePlan ClassifyValue(StableId id)
    {
        InteropType type = Underlying(id);
        if (m_calls.TryGetValue(id, out NativeCallValuePlan? known)) return known;
        Shape shape = Describe(type.id, [])
            ?? throw Unsupported(type, "Its fields, managed alignment or complete storage cannot be represented by this aggregate implementation.");
        ImmutableArray<string> classes;
        if (family == "dotnet.windows-x64")
            classes = [shape.Size is 1 or 2 or 4 or 8 ? "Integer" : "Indirect"];
        else if (shape.Size > 16)
            classes = ["Memory"];
        else
        {
            string[] words = Enumerable.Repeat("NoClass", checked((shape.Size + 7) / 8)).ToArray();
            bool aligned = Classify(type.id, 0, words, []);
            // Empty or pure-padding shapes have no independently represented source field classification.
            if (words.All(value => value == "NoClass")) throw Unsupported(type, "The record has no classifiable native fields.");
            classes = aligned ? words.ToImmutableArray() : ["Memory"];
        }
        NativeCallValuePlan plan = new(id, shape.Size, shape.Alignment, classes);
        m_calls.Add(id, plan);
        return plan;
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

    private bool Classify(
        StableId id,
        long offset,
        string[] words,
        HashSet<StableId> active
    ) {
        InteropType type = Underlying(id);
        if (!active.Add(type.id)) return false;
        try
        {
            if (type.category == NativeTypeCategory.Enumeration) return Classify(type.elementType!.Value, offset, words, active);
            if (type.category == NativeTypeCategory.Record)
            {
                bool aligned = true;
                foreach (InteropField field in type.fields)
                    aligned &= Classify(field.type, checked(offset + field.offset!.Value), words, active);
                return aligned;
            }
            if (type.category == NativeTypeCategory.Array)
            {
                long stride = Underlying(type.elementType!.Value).size!.Value;
                bool aligned = true;
                for (long index = 0; index < type.arrayLength; index++)
                    aligned &= Classify(type.elementType.Value, checked(offset + index * stride), words, active);
                return aligned;
            }
            int first = checked((int)(offset / 8)), last = checked((int)((offset + type.size!.Value - 1) / 8));
            if (first < 0 || last >= words.Length) throw new InvalidOperationException("An aggregate field exceeds the checked eightbyte storage extent.");
            string classification = type.primitiveKind is "Float" or "Double" ? "Sse" : "Integer";
            for (int index = first; index <= last; index++)
                words[index] = words[index] == "NoClass" ? classification : words[index] == classification ? classification : "Integer";
            return offset % type.alignment!.Value == 0;
        }
        finally { active.Remove(type.id); }
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
