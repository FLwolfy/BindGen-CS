using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;

namespace BGCS.Analysis.Safety;

/// <summary>Reads existing pointer facts without turning address presentation into ownership or calling-ABI evidence.</summary>
public sealed class NativePointerShapeReader
{
    private readonly InteropModule m_module;
    private readonly Dictionary<StableId, InteropType> m_types;

    /// <summary>Creates a detached reader for one immutable native ABI graph.</summary>
    /// <param name="module">The complete native graph.</param>
    public NativePointerShapeReader(InteropModule module)
    {
        m_module = module ?? throw new ArgumentNullException(nameof(module));
        m_types = module.types.ToDictionary(type => type.id);
    }

    /// <summary>Reads every pointer level, qualification and named alias identity ending at a native record or opaque void address.</summary>
    /// <param name="identity">An existing pointer or pointer alias.</param>
    /// <returns>The exact shape, or null when this identity is not a supported data address.</returns>
    public NativePointerShape? Read(StableId identity)
    {
        List<ImmutableArray<string>> qualifiers = [];
        List<string> current = [];
        List<string> aliases = [];
        HashSet<StableId> visited = [];
        StableId id = identity;
        int depth = 0;
        while (true)
        {
            if (!visited.Add(id)) throw new InvalidOperationException("A native pointer-address graph is cyclic.");
            InteropType type = m_types[id];
            if (type.qualifier is string qualifier)
            {
                if (qualifier is not ("Const" or "Volatile")) return null;
                if (!current.Contains(qualifier, StringComparer.Ordinal)) current.Add(qualifier);
            }
            if (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
            {
                if (type.elementType is not StableId element) return null;
                if (type.category == NativeTypeCategory.Alias && Canonical(element).category == NativeTypeCategory.Pointer)
                    aliases.Add(depth + ":" + type.id.value);
                id = element;
                continue;
            }
            qualifiers.Add(current.OrderBy(value => value, StringComparer.Ordinal).ToImmutableArray());
            current.Clear();
            if (type.category == NativeTypeCategory.Pointer)
            {
                if (type.size != m_module.pointerSize || type.alignment != m_module.pointerSize || type.elementType is not StableId element) return null;
                depth++;
                id = element;
                continue;
            }
            return depth > 0 && (type.category == NativeTypeCategory.Record || type.category == NativeTypeCategory.Primitive && type.primitiveKind == "Void")
                ? new(identity, type.id, depth, qualifiers.ToImmutableArray(), aliases.ToImmutableArray()) : null;
        }
    }

    /// <summary>Checks address conversion without removing any pointee qualifier or changing an opaque pointer alias.</summary>
    /// <param name="source">The existing source pointer.</param>
    /// <param name="destination">The existing destination pointer.</param>
    /// <returns>Whether native pointer conversion is supported by this finite projection.</returns>
    public bool CanConvert(
        StableId source,
        StableId destination
    ) {
        NativePointerShape? from = Read(source), to = Read(destination);
        if (from is null || to is null || from.record != to.record || from.depth != to.depth || !from.pointerAliases.SequenceEqual(to.pointerAliases)) return false;
        for (int level = 1; level <= from.depth; level++)
        {
            ImmutableArray<string> a = from.qualifiers[level], b = to.qualifiers[level];
            if (from.depth == 1 ? a.Except(b, StringComparer.Ordinal).Any() : !a.SequenceEqual(b)) return false;
        }
        return true;
    }

    private InteropType Canonical(StableId identity)
    {
        HashSet<StableId> visited = [];
        InteropType type = m_types[identity];
        while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
        {
            if (!visited.Add(type.id) || type.elementType is not StableId element) throw new InvalidOperationException("A native pointer alias is cyclic or incomplete.");
            type = m_types[element];
        }
        return type;
    }
}

/// <summary>Preserves exact pointer levels without claiming address validity, ownership or native allocation extent.</summary>
/// <param name="pointer">The original pointer identity.</param>
/// <param name="record">The final native record or primitive void identity.</param>
/// <param name="depth">The total pointer depth.</param>
/// <param name="qualifiers">Qualifications at the pointer value followed by each pointee level.</param>
/// <param name="pointerAliases">Named pointer aliases, including their exact graph level.</param>
public sealed record NativePointerShape(
    StableId pointer,
    StableId record,
    int depth,
    ImmutableArray<ImmutableArray<string>> qualifiers,
    ImmutableArray<string> pointerAliases
)
{
    /// <summary>Gets whether writing through the first dereference would remove native const.</summary>
    public bool readOnly => qualifiers[1].Contains("Const", StringComparer.Ordinal);
    /// <summary>Gets whether an unsafe C# view would need volatile native access semantics.</summary>
    public bool hasVolatilePointee => qualifiers.Skip(1).Any(level => level.Contains("Volatile", StringComparer.Ordinal));
}
