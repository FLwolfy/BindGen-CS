using System.Collections.Generic;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;

namespace BGCS.CSharp.Projection;

internal static class NativeCallableAliasFacts
{
    internal static InteropType? Resolve(
        InteropModule module,
        InteropType alias
    ) {
        if (alias.category != NativeTypeCategory.Alias) return null;
        HashSet<StableId> seen = [];
        InteropType type = alias;
        bool pointer = false;
        while (seen.Add(type.id))
        {
            if (type.category == NativeTypeCategory.Function) return type;
            if (type.elementType is not StableId element) return null;
            if (type.category == NativeTypeCategory.Pointer)
            {
                if (pointer) return null;
                pointer = true;
            }
            else if (type.category is not (NativeTypeCategory.Alias or NativeTypeCategory.Qualified)) return null;
            type = module.GetType(element);
        }
        return null;
    }
}
