using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Requests;

namespace BGCS.CSharp.Planning;

internal static class DefaultArgumentPlanner
{
    internal static CSharpEmissionPlan Apply(
        CSharpEmissionPlan plan,
        EmissionRequest request
    ) {
        if (plan.api.defaultOverloadLimit is < 1 or > 4096) throw new ArgumentException("The default overload limit must be between 1 and 4096.");
        if (plan.api.methods.Any(member => member.defaults.IsDefault)) throw new ArgumentException("Managed default arguments must be initialized.");
        ImmutableArray<CSharpFacadeMethodPlan> originals = plan.api.methods.Any(member => !member.defaults.IsEmpty)
            ? SharedContextPlanner.CreateMethods(plan, request) : [];
        var membersByExport = plan.api.methods.ToLookup(member => member.nativeExport);
        var functionsById = plan.api.interop.functions.ToLookup(function => function.id);
        Dictionary<string, CSharpDefaultConstantPlan> constants = new(StringComparer.Ordinal);
        HashSet<BGCS.Core.Common.StableId> covered = [];
        ImmutableArray<CSharpDefaultOverloadPlan>.Builder overloads = ImmutableArray.CreateBuilder<CSharpDefaultOverloadPlan>();
        foreach (CSharpFacadeMethodPlan signature in originals)
        {
            overloads.AddRange(Expand(signature, signature.name));
            if (overloads.Count > plan.api.defaultOverloadLimit) throw new ArgumentException("The context exceeds api.defaultOverloadLimit.");
        }
        if (plan.api.methods.Any(member => !member.defaults.IsDefaultOrEmpty && !covered.Contains(member.nativeExport)))
            throw new NotSupportedException("Explicit default arguments require an existing checked friendly operation; establish its missing value, buffer or lifetime contract first.");
        ImmutableArray<CSharpFunctionContainerPlan> containers = plan.functionContainers.Select(container =>
        {
            var methods = container.methods.ToBuilder();
            foreach (CSharpContainerMethodPlan method in container.methods)
                methods.AddRange(Expand(method.signature, method.targetName).Select(overload =>
                    new CSharpContainerMethodPlan(overload.targetName, overload.signature) { arguments = overload.arguments }));
            if (methods.Count - container.methods.Length > plan.api.defaultOverloadLimit) throw new ArgumentException("A named function view exceeds api.defaultOverloadLimit.");
            return container with { methods = methods.ToImmutable() };
        }).ToImmutableArray();
        return plan with
        {
            defaultOverloads = overloads.ToImmutable(),
            functionContainers = containers,
            defaultConstants = constants.Values.OrderBy(item => item.name, StringComparer.Ordinal).ToImmutableArray()
        };

        ImmutableArray<CSharpDefaultOverloadPlan> Expand(
            CSharpFacadeMethodPlan signature,
            string target
        ) {
            if (signature.nativeExport is not { } export) return [];
            ManagedMember member = membersByExport[export].Single();
            if (member.defaults.IsDefault) throw new ArgumentException("Managed default arguments must be initialized.");
            if (member.defaults.IsEmpty) return [];
            covered.Add(export);
            InteropFunction function = functionsById[export].Single();
            Dictionary<int, string> values = [];
            foreach (ManagedDefaultArgument argument in member.defaults)
            {
                int native = -1;
                for (int index = 0; index < function.parameters.Length; index++) if (function.parameters[index].id == argument.nativeParameter) native = index;
                int projected = signature.inputIndices.IndexOf(native);
                if (native >= 0 && projected < 0 && signature.boundNativeNullInputs.Contains(argument.nativeParameter)
                    && argument.value.kind == ManagedDefaultValueKind.Null && argument.value.value is null) continue;
                if (native < 0 || projected < 0) throw new ArgumentException("A default argument targets a hidden or non-input native parameter.");
                string type = signature.parameterTypes[projected];
                if (FacadeParameters.Modifier(signature, projected).Length != 0)
                    throw new NotSupportedException("A ref, out or in value cannot be omitted as a constant argument.");
                string expression = ManagedDefaultValuePlanner.Expression(argument.value, type, plan);
                if (argument.value.kind == ManagedDefaultValueKind.ConstantReference)
                {
                    string identity = type + "\n" + expression;
                    string name = "Default_" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
                    constants.TryAdd(name, new(name, type, expression));
                    expression = "global::" + CSharpNames.Namespace(plan.api.@namespace) + "." + plan.contextName + ".__BgcsDefaultArguments." + name;
                }
                if (!values.TryAdd(projected, expression))
                    throw new ArgumentException("A native parameter has multiple default arguments.");
            }
            if (values.Count > 12 || (1 << values.Count) - 1 > plan.api.defaultOverloadLimit)
                throw new ArgumentException("Default-argument expansion exceeds its explicit limit; select fewer defaults or increase api.defaultOverloadLimit up to 4096.");
            int[] positions = values.Keys.Order().ToArray();
            ImmutableArray<CSharpDefaultOverloadPlan>.Builder result = ImmutableArray.CreateBuilder<CSharpDefaultOverloadPlan>();
            for (int mask = 1; mask < 1 << positions.Length; mask++)
            {
                HashSet<int> omitted = positions.Where((
                    _,
                    index
                ) => (mask & (1 << index)) != 0).ToHashSet();
                int[] retained = Enumerable.Range(0, signature.parameterNames.Length).Where(index => !omitted.Contains(index)).ToArray();
                CSharpFacadeMethodPlan projected = signature with
                {
                    parameterNames = retained.Select(index => signature.parameterNames[index]).ToImmutableArray(),
                    parameterTypes = retained.Select(index => signature.parameterTypes[index]).ToImmutableArray(),
                    parameterModifiers = retained.Select(index => FacadeParameters.Modifier(signature, index)).ToImmutableArray(),
                    inputIndices = retained.Select(index => signature.inputIndices[index]).ToImmutableArray(),
                    documentationContent = signature.documentationContent is { } content
                        ? content with { parameters = retained.Select(index => content.parameters[index]).ToImmutableArray() } : null
                };
                result.Add(new(target, projected, Enumerable.Range(0, signature.parameterNames.Length)
                    .Select(index => omitted.Contains(index) ? values[index] : FacadeParameters.Argument(signature, index)).ToImmutableArray()));
                if (result.Count > plan.api.defaultOverloadLimit) throw new ArgumentException("A function view exceeds the configured default overload limit.");
            }
            return result.ToImmutable();
        }
    }
}

/// <summary>Plans one finite overload that forwards explicit constants through an existing checked friendly operation.</summary>
/// <param name="targetName">The original full-argument operation.</param>
/// <param name="signature">The public projected overload.</param>
/// <param name="arguments">The exact full argument list including typed constant expressions.</param>
public sealed record CSharpDefaultOverloadPlan(
    string targetName,
    CSharpFacadeMethodPlan signature,
    ImmutableArray<string> arguments
);

/// <summary>Requires a referenced consumer value to satisfy the language's compile-time constant rules.</summary>
/// <param name="name">The deterministic generated constant field.</param>
/// <param name="type">The exact projected parameter type.</param>
/// <param name="expression">The checked qualified constant reference.</param>
public sealed record CSharpDefaultConstantPlan(
    string name,
    string type,
    string expression
);
