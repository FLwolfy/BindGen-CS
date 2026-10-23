using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;

namespace BGCS.CSharp.Planning;

internal static class OwnedObjectPlanner
{
    internal static CSharpEmissionPlan Plan(CSharpEmissionPlan plan)
    {
        Dictionary<(StableId? native, StableId? generated), string> objects = [];
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
            if (record.category != NativeTypeCategory.Record) continue;
            if (record.sourceNativeType is null && record.sourceGeneratedType is null) continue;
            if (record.sourceNativeType is not null && record.sourceGeneratedType is not null)
                throw new InvalidOperationException("An owned record cannot be both an original native type and a generated bridge owner.");
            var identity = (record.sourceNativeType, record.sourceGeneratedType);
            if (!objects.TryGetValue(identity, out string? name))
            {
                string storage = plan.types.Single(type => type.nativeType == record.id).declaredName!;
                name = CSharpNames.Identifier(storage.TrimStart('@') + "Object");
                if (!reserved.Add(name.TrimStart('@'))) throw new ArgumentException($"Owned object name '{name}' conflicts with an API member.");
                objects.Add(identity, name);
            }
            methods[index] = status with { ownedResult = owned with { objectName = name } };
        }
        ImmutableArray<CSharpObjectPlan>.Builder result = ImmutableArray.CreateBuilder<CSharpObjectPlan>();
        foreach (var (identity, name) in objects.OrderBy(pair => pair.Value, StringComparer.Ordinal))
        {
            var selected = Enumerable.Range(0, methods.Count).Where(index => methods[index].instanceIndex >= 0
                && Matches(plan.api.interop.functions.Single(function => function.id == plan.methods[methods[index].methodIndex].nativeExport))).ToImmutableArray();
            bool Matches(InteropFunction function) => identity.native is not null
                ? function.sourceOwnerType == identity.native && function.sourceGeneratedOwnerType is null
                : function.sourceGeneratedOwnerType == identity.generated && function.sourceOwnerType is null;
            HashSet<string> memberNames = new(StringComparer.Ordinal)
            { name.TrimStart('@'), "releaseFailure", "releaseDomain", "isClosed", "m_context", "m_handle" };
            foreach (int index in selected)
                if (memberNames.Contains(plan.methods[methods[index].methodIndex].name.TrimStart('@')))
                    throw new ArgumentException("An owned object method conflicts with its resource lifecycle API.");
            result.Add(new(name, identity.native, selected) { generatedType = identity.generated });
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
