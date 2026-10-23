using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Analysis.Safety;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Managed;

namespace BGCS.CSharp.Planning;

internal static class NullDefaultMethodPlanner
{
    internal static CSharpEmissionPlan Apply(CSharpEmissionPlan plan)
    {
        ImmutableArray<CSharpNullDefaultMethodPlan>.Builder operations = ImmutableArray.CreateBuilder<CSharpNullDefaultMethodPlan>();
        var membersByExport = plan.api.methods.ToLookup(member => member.nativeExport);
        var functionsById = plan.api.interop.functions.ToLookup(function => function.id);
        for (int methodIndex = 0; methodIndex < plan.methods.Length; methodIndex++)
        {
            CSharpMethodPlan method = plan.methods[methodIndex];
            ManagedMember member = membersByExport[method.nativeExport].Single();
            if (member.defaults.IsDefaultOrEmpty || !member.defaults.Any(value => value.value.kind == ManagedDefaultValueKind.Null)) continue;
            InteropFunction function = functionsById[method.nativeExport].Single();
            if (OverloadPlanner.IsDirect(plan, method, function)) continue;
            if (function.error is not null || function.asynchronous is not null || function.returnOwnership is not null || function.returnString is not null
                || function.sourceOwnerType is not null || function.sourceGeneratedOwnerType is not null || member.receiver is not null || function.isVariadic || !function.hasPrototype
                || !OverloadPlanner.SupportsDirectResult(plan, function.returnType)) continue;
            ImmutableArray<CSharpNullDefaultInputPlan>.Builder bindings = ImmutableArray.CreateBuilder<CSharpNullDefaultInputPlan>();
            for (int index = 0; index < function.parameters.Length; index++)
            {
                if (OverloadPlanner.SupportsDirectInput(plan, method, function, index)) continue;
                InteropParameter parameter = function.parameters[index];
                ManagedDefaultArgument[] defaults = member.defaults.Where(argument => argument.nativeParameter == parameter.id).ToArray();
                if (defaults.Length != 1 || defaults[0].value.kind != ManagedDefaultValueKind.Null || defaults[0].value.value is not null
                    || method.valueProjections.ContainsKey(index) || !method.parameterTypes[index].EndsWith('*')
                    || new ValueParameterAnalyzer().GetNullBindingRestriction(plan.api.interop, function, parameter) is not null)
                    throw new NotSupportedException("A NULL-only operation requires every unsupported pointer to have an exact null default and an explicit nullable, synchronous, nonretaining single-value contract; its full pointer form remains in Raw.");
                bindings.Add(new(index, parameter.id, method.parameterTypes[index]));
            }
            if (bindings.Count == 0) continue;
            HashSet<int> hidden = bindings.Select(binding => binding.parameterIndex).ToHashSet();
            int[] visible = OverloadPlanner.PublicInputs(method, Enumerable.Range(0, function.parameters.Length)).Where(index => !hidden.Contains(index)).ToArray();
            CSharpFacadeMethodPlan signature = new(method.name,
                method.projectedReturnType,
                visible.Select(index => OverloadPlanner.InputType(method, index)).ToImmutableArray(), visible.Select(index => method.parameterNames[index]).ToImmutableArray())
            {
                nativeExport = method.nativeExport,
                inputIndices = visible.ToImmutableArray(),
                boundNativeNullInputs = bindings.Select(binding => binding.nativeParameter).ToImmutableArray(),
                parameterModifiers = visible.Select(index => OverloadPlanner.InputModifier(method, index)).ToImmutableArray(),
                documentation = method.documentation,
                documentationContent = method.documentationContent is { } documentation ? documentation with { parameters = visible.Select(index => documentation.parameters[index]).ToImmutableArray() } : null
            };
            operations.Add(new(methodIndex, bindings.ToImmutable(), signature));
        }
        return plan with { nullDefaultMethods = operations.ToImmutable() };
    }
}

/// <summary>Plans a checked synchronous facade that binds explicit nullable data pointers only to constant null.</summary>
/// <param name="methodIndex">The unchanged original raw import index.</param>
/// <param name="nullInputs">Exact native pointer channels omitted from the public facade.</param>
/// <param name="signature">The remaining checked friendly inputs and original native identity.</param>
public sealed record CSharpNullDefaultMethodPlan(
    int methodIndex,
    ImmutableArray<CSharpNullDefaultInputPlan> nullInputs,
    CSharpFacadeMethodPlan signature
);

/// <summary>Records one fixed null argument without granting any non-null pointer borrowing capability.</summary>
/// <param name="parameterIndex">The exact native argument position.</param>
/// <param name="nativeParameter">The original parameter identity.</param>
/// <param name="rawPointerType">The checked raw data-pointer syntax.</param>
public sealed record CSharpNullDefaultInputPlan(
    int parameterIndex,
    StableId nativeParameter,
    string rawPointerType
);
