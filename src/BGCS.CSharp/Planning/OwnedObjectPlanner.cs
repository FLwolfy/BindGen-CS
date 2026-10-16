using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Native;

namespace BGCS.CSharp.Planning;

internal static class OwnedObjectPlanner
{
    internal static CSharpEmissionPlan Plan(CSharpEmissionPlan plan)
    {
        Dictionary<StableId, string> objects = [];
        HashSet<string> reserved = new(plan.methods.Select(method => method.name.TrimStart('@')), StringComparer.Ordinal)
        { plan.contextName.TrimStart('@'), "RawApi", "Raw", "NativeImports", "Load", "Create", "Dispose", "DisposeAsync", "FromProvider" };
        var methods = plan.statusMethods.ToBuilder();
        for (int index = 0; index < methods.Count; index++)
        {
            CSharpStatusMethodPlan status = methods[index];
            if (status.ownedResult is not CSharpOwnedResultPlan owned) continue;
            InteropFunction function = plan.api.interop.functions.Single(function => function.id == plan.methods[status.methodIndex].nativeExport);
            InteropType output = Resolve(function.parameters[status.resultIndex].type);
            InteropType value = Resolve(output.elementType!.Value);
            if (value.elementType is not StableId element) continue;
            InteropType record = Resolve(element);
            if (record.sourceNativeType is not StableId native || record.category != NativeTypeCategory.Record) continue;
            if (!objects.TryGetValue(native, out string? name))
            {
                string storage = plan.types.Single(type => type.nativeType == record.id).declaredName!;
                name = CSharpNames.Identifier(storage.TrimStart('@') + "Object");
                if (!reserved.Add(name.TrimStart('@'))) throw new ArgumentException($"Owned object name '{name}' conflicts with an API member.");
                objects.Add(native, name);
            }
            methods[index] = status with { ownedResult = owned with { objectName = name } };
        }
        ImmutableArray<CSharpObjectPlan>.Builder result = ImmutableArray.CreateBuilder<CSharpObjectPlan>();
        foreach ((StableId native, string name) in objects.OrderBy(pair => pair.Value, StringComparer.Ordinal))
        {
            var selected = Enumerable.Range(0, methods.Count).Where(index => methods[index].instanceIndex >= 0
                && plan.api.interop.functions.Single(function => function.id == plan.methods[methods[index].methodIndex].nativeExport).sourceOwnerType == native).ToImmutableArray();
            HashSet<string> memberNames = new(StringComparer.Ordinal)
            { name.TrimStart('@'), "releaseFailure", "releaseDomain", "isClosed", "m_context", "m_handle" };
            foreach (int index in selected)
                if (memberNames.Contains(plan.methods[methods[index].methodIndex].name.TrimStart('@')))
                    throw new ArgumentException("An owned object method conflicts with its resource lifecycle API.");
            result.Add(new(name, native, selected));
        }
        return plan with { statusMethods = methods.ToImmutable(), objects = result.ToImmutable() };

        InteropType Resolve(StableId id)
        {
            InteropType type = plan.api.interop.GetType(id);
            while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified) type = plan.api.interop.GetType(type.elementType!.Value);
            return type;
        }
    }
}
