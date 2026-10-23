using System.Collections.Immutable;
using System.Linq;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;

namespace BGCS.CSharp.Projection;

internal static class ManagedBooleanProjection
{
    private static readonly ManagedRecipeProjection s_input = new("bool", "{value} ? (byte)1 : (byte)0", null);
    private static readonly ManagedRecipeProjection s_output = new("bool", null, "{value} != 0");

    internal static ManagedApiModel Apply(ManagedApiModel model)
    {
        var functionsById = model.interop.functions.ToLookup(function => function.id);
        return model with
        {
            methods = model.methods.Select(member =>
            {
                InteropFunction function = functionsById[member.nativeExport].Single();
                ImmutableArray<ManagedValueProjection>.Builder values = member.valueProjections.ToBuilder();
                foreach (InteropParameter parameter in function.parameters)
                {
                    if (values.Any(value => value.nativeParameter == parameter.id)) continue;
                    if (function.error?.valueParameter == parameter.id)
                    {
                        InteropType result = Resolve(model.interop, parameter.type);
                        if (result.category == NativeTypeCategory.Pointer && result.elementType is StableId element && Boolean(model.interop, element))
                            values.Add(new(parameter.id, s_output));
                    }
                    else if (function.error?.errorParameter != parameter.id && Boolean(model.interop, parameter.type))
                        values.Add(new(parameter.id, s_input));
                }
                return member with
                {
                    valueProjections = values.ToImmutable(),
                    returnProjection = member.returnProjection ?? (function.error is null && Boolean(model.interop, function.returnType) ? s_output : null)
                };
            }).ToImmutableArray()
        };
    }

    private static bool Boolean(
        InteropModule module,
        StableId identity
    ) {
        InteropType type = Resolve(module, identity);
        return type.category == NativeTypeCategory.Primitive && type.primitiveKind == "Bool" && type.size == 1 && type.alignment == 1;
    }

    private static InteropType Resolve(
        InteropModule module,
        StableId identity
    ) {
        InteropType type = module.GetType(identity);
        System.Collections.Generic.HashSet<StableId> seen = [];
        while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified && type.elementType is StableId element)
        {
            if (!seen.Add(type.id)) return type;
            type = module.GetType(element);
        }
        return type;
    }
}
