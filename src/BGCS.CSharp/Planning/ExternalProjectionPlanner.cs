using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Native;

namespace BGCS.CSharp.Planning;

internal static class ExternalProjectionPlanner
{
    internal static CSharpEmissionPlan Apply(CSharpEmissionPlan plan)
    {
        if (plan.api.externalTypeProjections.IsDefault) throw new ArgumentException("External projections must be initialized.");
        if (plan.api.externalTypeProjections.IsEmpty) return plan;
        Dictionary<StableId, CSharpRecipeProjectionPlan> conversions = [];
        ImmutableArray<CSharpExternalTypeProjectionPlan>.Builder copies = ImmutableArray.CreateBuilder<CSharpExternalTypeProjectionPlan>();
        foreach (var projection in plan.api.externalTypeProjections)
        {
            CSharpTypePlan type = plan.types.Single(type => type.nativeType == projection.nativeType);
            CSharpStoragePlan storage = plan.storage.Single(item => item.name == type.declaredName);
            var names = storage.fields.Where(field => field.nativeField.HasValue).ToDictionary(field => field.nativeField!.Value, field => field.name);
            CSharpExternalTypeProjectionPlan copy = new ExternalCarrierPlanner().Plan(plan.api.interop, projection, type.syntax, names);
            if (!conversions.TryAdd(copy.nativeType, new("external_" + copy.id, copy.managedType, copy.rawCarrierType,
                "__ToExternalNative_" + copy.id + "(value)", "__FromExternalNative_" + copy.id + "(value)", null)))
                throw new ArgumentException("One native record cannot have multiple external consumer projections.");
            copies.Add(copy);
        }
        ImmutableArray<CSharpValueParameterOmissionPlan>.Builder omissions = plan.valueParameterOmissions.ToBuilder();
        ImmutableArray<CSharpMethodPlan> methods = plan.methods.Select(method =>
        {
            InteropFunction function = plan.api.interop.functions.Single(function => function.id == method.nativeExport);
            var inputs = method.valueProjections.ToBuilder();
            var values = method.valueParameters.ToBuilder();
            for (int index = 0; index < function.parameters.Length; index++)
            {
                InteropType native = Resolve(function.parameters[index].type);
                if ((function.error?.valueParameter == function.parameters[index].id || method.valueParameters.Any(value => value.parameterIndex == index))
                    && native.category == NativeTypeCategory.Pointer)
                    native = Resolve(native.elementType!.Value);
                if (!conversions.TryGetValue(native.id, out CSharpRecipeProjectionPlan? conversion)) continue;
                CSharpValueParameterPlan? borrowed = method.valueParameters.SingleOrDefault(value => value.parameterIndex == index);
                if (borrowed is not null)
                {
                    omissions.Add(new(function.id, function.parameters[index].id, function.parameters[index].source ?? function.source,
                        "An external field-copy projection cannot preserve the original in/ref/out address or overlapping aliases; use the raw pointer API or an explicit value-copy native adapter."));
                    values.Remove(borrowed);
                    continue;
                }
                if (method.stringInputs.Any(input => input.parameterIndex == index) || method.bufferInputs.Any(input => input.parameterIndex == index)
                    || !inputs.TryAdd(index, conversion))
                    throw new ArgumentException("An external record projection conflicts with another explicit conversion.");
            }
            CSharpRecipeProjectionPlan? returned = method.returnProjection;
            if (conversions.TryGetValue(Resolve(function.returnType).id, out CSharpRecipeProjectionPlan? direct))
            {
                if (returned is not null) throw new ArgumentException("An external return projection conflicts with an explicit conversion.");
                returned = direct;
            }
            return method with { valueProjections = inputs.ToImmutable(), returnProjection = returned, valueParameters = values.ToImmutable() };
        }).ToImmutableArray();
        return plan with
        {
            methods = methods,
            externalTypeProjections = copies.ToImmutable(),
            recipeProjections = plan.recipeProjections.AddRange(conversions.Values.OrderBy(item => item.id, StringComparer.Ordinal)),
            valueParameterOmissions = omissions.ToImmutable()
        };

        InteropType Resolve(StableId id)
        {
            InteropType type = plan.api.interop.GetType(id);
            while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified) type = plan.api.interop.GetType(type.elementType!.Value);
            return type;
        }
    }
}
