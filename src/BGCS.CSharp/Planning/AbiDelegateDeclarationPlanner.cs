using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.CSharp.Projection;

namespace BGCS.CSharp.Planning;

/// <summary>Declares a named exact native delegate independently of callback registration or lifetime inference.</summary>
/// <param name="nativeType">The original named native typedef identity.</param>
/// <param name="callableType">The exact underlying fixed native prototype.</param>
/// <param name="name">The independently selected managed type name.</param>
/// <param name="returnType">The checked raw callback result carrier.</param>
/// <param name="parameterTypes">The checked raw callback argument carriers.</param>
/// <param name="callingConvention">The exact implemented native reverse-call convention.</param>
public sealed record CSharpAbiDelegatePlan(
    StableId nativeType,
    StableId callableType,
    string name,
    string returnType,
    ImmutableArray<string> parameterTypes,
    string callingConvention
)
{
    /// <summary>Gets inert typedef documentation without treating source comments as generated code.</summary>
    public string? documentation { get; init; }
}

internal static class AbiDelegateDeclarationPlanner
{
    internal static ImmutableArray<CSharpAbiDelegatePlan> Plan(CSharpEmissionPlan plan)
    {
        var result = ImmutableArray.CreateBuilder<CSharpAbiDelegatePlan>();
        Dictionary<StableId, string> carriers = plan.types.ToDictionary(type => type.nativeType, type => type.syntax);
        Dictionary<StableId, string> names = plan.api.types.ToDictionary(type => type.nativeType, type => type.name);
        HashSet<string> allocated = plan.storage.Select(type => type.name.TrimStart('@')).Concat(plan.managedEnums.Select(type => type.name.TrimStart('@')))
            .Append(plan.contextName.TrimStart('@')).Append(plan.api.name.TrimStart('@')).ToHashSet(StringComparer.Ordinal);
        foreach (InteropType alias in plan.api.interop.types.Where(type => !string.IsNullOrWhiteSpace(type.declarationName)))
        {
            InteropType? callable = NativeCallableAliasFacts.Resolve(plan.api.interop, alias);
            if (callable is null || !carriers[callable.id].StartsWith("delegate* unmanaged[", StringComparison.Ordinal)) continue;
            string name = CSharpNames.Identifier(names[alias.id]);
            if (!allocated.Add(name.TrimStart('@'))) throw new ArgumentException("A named ABI callback type conflicts with another delegate, storage or API declaration.");
            string convention = callable.callingConvention switch
            {
                "C" or "Cdecl" or "Default" => "Cdecl",
                "X86StdCall" or "StdCall" or "Stdcall" => "Stdcall",
                "X86FastCall" or "FastCall" or "Fastcall" => "Fastcall",
                "X86ThisCall" or "ThisCall" or "Thiscall" => "Thiscall",
                _ => throw new InvalidOperationException("A checked function-pointer carrier lost its exact delegate convention.")
            };
            result.Add(new(alias.id, callable.id, name, carriers[callable.returnType!.Value], callable.parameterTypes.Select(id => carriers[id]).ToImmutableArray(), convention)
            { documentation = DocumentationPlaceholderPlanner.Fill(alias.documentation, plan.api.emitPlaceholderComments) });
        }
        return result.ToImmutable();
    }
}
