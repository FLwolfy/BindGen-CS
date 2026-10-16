using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Native;
using BGCS.Contracts.Targets;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BGCS.CSharp.Planning;

/// <summary>Plans exact function-pointer address transport at a supported runtime boundary without changing the public raw callable contract.</summary>
/// <param name="methodIndex">The original native import index.</param>
/// <param name="declarationName">The physical extern declaration identity.</param>
/// <param name="fieldName">The physical function-table field identity.</param>
/// <param name="returnType">The physical return carrier.</param>
/// <param name="parameterTypes">The physical arguments in original ABI order.</param>
/// <param name="addressParameters">Direct function-pointer arguments explicitly cast to a native integer.</param>
/// <param name="addressResult">Whether a native integer result is explicitly cast back to the original function-pointer type.</param>
public sealed record CSharpPhysicalImportPlan(
    int methodIndex,
    string declarationName,
    string fieldName,
    string returnType,
    ImmutableArray<string> parameterTypes,
    ImmutableArray<int> addressParameters,
    bool addressResult
);

internal static class PhysicalImportPlanner
{
    internal static CSharpEmissionPlan Apply(CSharpEmissionPlan plan) => plan with { physicalImports = Plan(plan) };

    internal static CSharpPhysicalImportPlan? Find(
        CSharpEmissionPlan plan,
        int methodIndex
    )
        => plan.physicalImports.FirstOrDefault(item => item.methodIndex == methodIndex);

    internal static ImmutableArray<CSharpPhysicalImportPlan> Plan(CSharpEmissionPlan plan)
    {
        ManagedFunctionPointerTransport transport = plan.managedRuntime?.functionPointerTransport ?? ManagedFunctionPointerTransport.Typed;
        if (!Enum.IsDefined(transport)) throw new NotSupportedException("The managed function-pointer import transport is undefined.");
        if (transport == ManagedFunctionPointerTransport.Typed)
        {
            if (plan.managedRuntime?.kind == ManagedRuntimeKind.BrowserWasm && plan.methods.Any(method =>
                SyntaxFactory.ParseTypeName(method.returnType) is FunctionPointerTypeSyntax
                || method.parameterTypes.Any(type => SyntaxFactory.ParseTypeName(type) is FunctionPointerTypeSyntax)))
                throw new NotSupportedException("Browser function-pointer imports require the explicitly implemented wasm32 function-table index transport capability.");
            return [];
        }
        if (plan.managedRuntime is not { kind: ManagedRuntimeKind.BrowserWasm, moduleMechanism: NativeModuleMechanism.StaticAdapter }
            || plan.api.interop.pointerSize != 4 || plan.api.interop.byteOrder != NativeByteOrder.LittleEndian
            || plan.methods.Any(method => method.callingConvention != "Cdecl"))
            throw new NotSupportedException("Function-table index transport requires the browser wasm32 static C ABI and exact Cdecl signatures.");
        var result = ImmutableArray.CreateBuilder<CSharpPhysicalImportPlan>();
        for (int index = 0; index < plan.methods.Length; index++)
        {
            CSharpMethodPlan method = plan.methods[index];
            InteropFunction function = plan.api.interop.functions.Single(item => item.id == method.nativeExport);
            var arguments = ImmutableArray.CreateBuilder<int>();
            var carriers = method.parameterTypes.ToBuilder();
            for (int parameter = 0; parameter < function.parameters.Length; parameter++)
                if (IsDirectFunctionAddress(function.parameters[parameter].type, method.parameterTypes[parameter]))
                { arguments.Add(parameter); carriers[parameter] = "nint"; }
            bool returned = IsDirectFunctionAddress(function.returnType, method.returnType);
            if (returned || arguments.Count != 0)
                result.Add(new(index, "PhysicalFunction" + index, "m_nativeFunction" + index,
                    returned ? "nint" : method.returnType, carriers.ToImmutable(), arguments.ToImmutable(), returned));
        }
        return result.ToImmutable();

        bool IsDirectFunctionAddress(
            StableId id,
            string syntax
        ) {
            if (SyntaxFactory.ParseTypeName(syntax) is not FunctionPointerTypeSyntax) return false;
            InteropType type = Resolve(id);
            if (type.category == NativeTypeCategory.Pointer && type.elementType is StableId element) type = Resolve(element);
            if (type.category != NativeTypeCategory.Function)
                throw new InvalidOperationException("A typed function-pointer carrier has no matching native callable identity.");
            return true;
        }

        InteropType Resolve(StableId id)
        {
            HashSet<StableId> visited = [];
            InteropType type = plan.api.interop.GetType(id);
            while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
            {
                if (!visited.Add(type.id) || type.elementType is not StableId next)
                    throw new InvalidOperationException("A native function-pointer alias graph is cyclic or incomplete.");
                type = plan.api.interop.GetType(next);
            }
            return type;
        }
    }
}
