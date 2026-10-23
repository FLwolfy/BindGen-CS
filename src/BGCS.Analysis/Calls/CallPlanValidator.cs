using System;
using System.Collections.Generic;
using System.Linq;
using BGCS.Core.Calls;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using BGCS.Core.Targets;

namespace BGCS.Analysis.Calls;

/// <summary>Checks complete call plans against exact detached facts without classifying a platform ABI.</summary>
public sealed class CallPlanValidator
{
    private readonly InteropModule m_module;
    private readonly ManagedRuntimeProfile m_runtime;
    private readonly ContentDigest m_profileDigest;

    /// <summary>Binds independent plan validation to one immutable physical graph and execution profile.</summary>
    /// <param name="module">The final physical ABI graph.</param>
    /// <param name="runtime">The selected execution profile.</param>
    public CallPlanValidator(
        InteropModule module,
        ManagedRuntimeProfile runtime
    ) {
        VerifyProfile(module, runtime);
        m_module = module;
        m_runtime = runtime;
        m_profileDigest = ManagedRuntimeProfileIdentity.Capture(runtime);
    }

    /// <summary>Rejects an incomplete, mismatched or structurally changed direct-call plan against the bound immutable inputs.</summary>
    /// <param name="plan">The independently supplied direct-call plan.</param>
    public void Verify(NativeCallPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        InteropModule module = m_module;
        ManagedRuntimeProfile runtime = m_runtime;
        if (plan.nativeTargetId != module.targetId || plan.runtimeId != runtime.runtimeId || plan.runtimeProfileId != runtime.id
            || plan.provider != runtime.callAbi || plan.pointerSize != module.pointerSize || plan.byteOrder != module.byteOrder
            || plan.nativeTriple != runtime.nativeTriple || plan.runtimeProfileDigest != m_profileDigest)
            throw new InvalidOperationException("A direct-call plan does not belong to the selected target, runtime or ABI provider.");
        ArgumentNullException.ThrowIfNull(plan.signature);
        NativeCallSignature expected = plan.signature.direction switch
        {
            NativeCallDirection.Forward => NativeCallSignature.FromFunction(PhysicalFunction(module, plan.signature.id)),
            NativeCallDirection.ReverseCallback => NativeCallSignature.FromCallback(module.GetType(plan.signature.id)),
            _ => throw new InvalidOperationException("A direct-call plan has an undefined direction.")
        };
        if (!SameSignature(expected, plan.signature) || plan.parameters.IsDefault || plan.parameters.Length != expected.parameterTypes.Length)
            throw new InvalidOperationException("A direct-call plan changed the physical callable signature.");
        InteropType result = Resolve(module, expected.returnType);
        if (result.category == NativeTypeCategory.Primitive && result.primitiveKind == "Void")
        {
            if (plan.returnValue is not null) throw new InvalidOperationException("A void result cannot have a physical value carrier.");
        }
        else VerifyValue(module, expected.returnType, plan.returnValue);
        for (int index = 0; index < expected.parameterTypes.Length; index++) VerifyValue(module, expected.parameterTypes[index], plan.parameters[index]);
    }

    /// <summary>Validates execution facts without assigning capabilities to target names.</summary>
    /// <param name="module">The established native type graph.</param>
    /// <param name="runtime">The explicitly selected runtime profile.</param>
    public static void VerifyProfile(
        InteropModule module,
        ManagedRuntimeProfile runtime
    ) {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(runtime);
        if (string.IsNullOrWhiteSpace(runtime.id) || string.IsNullOrWhiteSpace(runtime.runtimeId.value)
            || string.IsNullOrWhiteSpace(runtime.callAbi.value) || string.IsNullOrWhiteSpace(runtime.nativeTriple)
            || runtime.nativeTargetId != module.targetId || runtime.pointerSize != module.pointerSize
            || !Enum.IsDefined(runtime.byteOrder) || runtime.byteOrder == NativeByteOrder.Unknown || runtime.byteOrder != module.byteOrder
            || !Enum.IsDefined(runtime.moduleMechanism) || !Enum.IsDefined(runtime.functionPointerTransport)
            || !Enum.IsDefined(runtime.libraryImportConvention) || runtime.importModes.IsDefaultOrEmpty
            || runtime.importModes.Any(mode => !Enum.IsDefined(mode)) || runtime.importModes.Distinct().Count() != runtime.importModes.Length)
            throw new NotSupportedException("The execution profile is incomplete or does not match the native target facts.");
        if (runtime.libraryImportConvention == ManagedImportConventionStrategy.RuntimeDefault && string.IsNullOrWhiteSpace(runtime.defaultCallingConvention))
            throw new NotSupportedException("Runtime-default imports require an explicitly established calling convention.");
    }

    /// <summary>Compares signature contents rather than immutable-array backing identity.</summary>
    /// <param name="first">The established signature.</param>
    /// <param name="second">The candidate signature.</param>
    /// <returns>Whether every physical signature fact matches.</returns>
    public static bool SameSignature(
        NativeCallSignature first,
        NativeCallSignature second
    )
        => first.id == second.id && first.returnType == second.returnType && first.callingConvention == second.callingConvention
            && first.direction == second.direction && first.isVariadic == second.isVariadic && first.hasPrototype == second.hasPrototype
            && !first.parameterTypes.IsDefault && !second.parameterTypes.IsDefault && first.parameterTypes.SequenceEqual(second.parameterTypes);

    private static InteropFunction PhysicalFunction(
        InteropModule module,
        StableId identity
    ) {
        try { return module.GetFunction(identity); }
        catch (KeyNotFoundException failure)
        { throw new InvalidOperationException("A direct-call plan references a missing physical function identity: " + identity.value + ".", failure); }
    }

    private static void VerifyValue(
        InteropModule module,
        StableId expected,
        NativeCallValuePlan? value
    ) {
        InteropType type = Resolve(module, expected);
        if (value is null || value.nativeType != expected || value.classes.IsDefaultOrEmpty || value.classes.Any(string.IsNullOrWhiteSpace)
            || type.size is not > 0 || type.alignment is not > 0 || value.size != type.size || value.alignment != type.alignment)
            throw new InvalidOperationException("A direct-call value plan changed an established type or storage fact.");
    }

    private static InteropType Resolve(
        InteropModule module,
        StableId id
    ) {
        HashSet<StableId>? seen = null;
        InteropType type = module.GetType(id);
        while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
        {
            if (!(seen ??= []).Add(type.id) || type.elementType is not StableId next) throw new InvalidOperationException("A direct-call type has cyclic or incomplete aliases.");
            type = module.GetType(next);
        }
        return type;
    }
}
