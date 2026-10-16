using System.Collections.Generic;
using BGCS.Contracts.Common;
using BGCS.Contracts.Native;

namespace BGCS.Analysis.Declarations;

internal static class ObjectiveCFactDependencies
{
    internal static IEnumerable<StableId> Declarations(NativeDeclaration declaration)
    {
        if (declaration.objectiveCContainer is { } container)
        {
            foreach (StableId protocol in container.protocols) yield return protocol;
            if (container.baseInterface is StableId baseInterface) yield return baseInterface;
            if (container.categoryTarget is StableId target) yield return target;
        }
        if (declaration.objectiveCProperty is { } property)
        {
            if (property.getter is StableId getter) yield return getter;
            if (property.setter is StableId setter) yield return setter;
        }
    }
}
