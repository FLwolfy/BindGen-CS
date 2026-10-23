using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using BGCS.Core.Targets;

namespace BGCS.CSharp.Planning;

// One reverse-call ABI and fallback planner serves call, registration and operation lifetimes.
internal sealed class ReverseCallbackPlanner
{
    private readonly CSharpEmissionPlan plan;
    private readonly InteropModule module;
    private readonly Dictionary<StableId, string> carriers;

    internal ReverseCallbackPlanner(CSharpEmissionPlan plan)
    {
        this.plan = plan;
        module = plan.api.interop;
        carriers = plan.types.ToDictionary(type => type.nativeType, type => type.syntax);
    }

    internal CSharpCallbackInputPlan PlanInput(
        InteropFunction function,
        CSharpMethodPlan method,
        int index,
        HashSet<int> userDataIndices,
        bool requireStatic = false
    ) {
        InteropParameter parameter = function.parameters[index];
        CallbackContract contract = parameter.callback ?? throw new InvalidOperationException("The callback protocol is absent.");
        if (contract.requiredResultOutputIndex is not null)
            throw new NotSupportedException("This normalized callback requires complete result-output initialization on every invocation; a managed failure-write protocol must be explicitly implemented before a friendly callback can be generated.");
        if (parameter.ownership is not null || parameter.text is not null || parameter.buffer is not null
            || parameter.singleValue is not null || method.valueProjections.ContainsKey(index))
            throw new NotSupportedException("A callback parameter cannot simultaneously replace another native storage or value-conversion protocol.");
        InteropType callable = Resolve(parameter.type);
        if (callable.category == NativeTypeCategory.Pointer && callable.elementType is StableId element) callable = Resolve(element);
        if (callable.category != NativeTypeCategory.Function || callable.id != contract.callableType)
            throw new InvalidOperationException("A callback contract no longer identifies its exact native function-pointer prototype.");
        if (!Strong(contract.evidence) || contract.lifetime is null || !Strong(contract.lifetime.evidence)
            || contract.threading is not (NativeCallbackThreading.CallerThread or NativeCallbackThreading.AnyThread or NativeCallbackThreading.EventLoop))
            throw new NotSupportedException("A managed callback requires explicit protocol evidence and known native callback threads.");
        if (!callable.hasPrototype || callable.isVariadic || callable.returnType is not StableId callbackResult
            || callable.parameterTypes.IsDefault || !Channel(callbackResult, allowVoid: true)
            || callable.parameterTypes.Any(type => !Channel(type, allowVoid: false)))
            throw new NotSupportedException("A managed callback requires a complete fixed scalar, enum or borrowed-pointer reverse-call signature.");
        string convention = Convention(callable.callingConvention);
        string resultType = Carrier(callbackResult);
        int userData = -1, callbackUserData = -1;
        bool staticThunk = contract.userDataParameter is not null;
        if (staticThunk)
        {
            userData = Array.FindIndex(function.parameters.ToArray(), candidate => candidate.id == contract.userDataParameter);
            callbackUserData = contract.callableUserDataIndex ?? -1;
            if (userData < 0 || userData == index || !userDataIndices.Add(userData)
                || callbackUserData < 0 || callbackUserData >= callable.parameterTypes.Length
                || !VoidPointer(function.parameters[userData].type) || !VoidPointer(callable.parameterTypes[callbackUserData]))
                throw new NotSupportedException("A static callback thunk requires distinct exact outer and callback-side void-pointer user-data channels.");
            InteropParameter state = function.parameters[userData];
            if (state.callback is not null || state.ownership is not null || state.text is not null || state.buffer is not null || state.singleValue is not null)
                throw new NotSupportedException("Generated callback user-data cannot replace another native parameter protocol.");
        }
        else if (requireStatic || contract.callableUserDataIndex is not null || plan.managedRuntime?.requiresStaticCallbackThunks == true)
            throw new NotSupportedException("This callback protocol requires an explicit user-data channel and a generated static callback thunk.");
        string digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(function.id.value + ":" + parameter.id.value)))[..10];
        string name = method.name.TrimStart('@') + method.parameterNames[index].TrimStart('@') + "Handler_" + digest;
        return new(index, userData, callbackUserData, CSharpNames.Identifier(name), "__BgcsCallbackState_" + digest,
            "__BgcsCallbackThunk_" + digest, resultType, callable.parameterTypes.Select(Carrier).ToImmutableArray(),
            Enumerable.Range(0, callable.parameterTypes.Length).Where(position => position != callbackUserData).ToImmutableArray(),
            convention, Fallback(contract, callbackResult, resultType), staticThunk)
        { abiDelegateType = staticThunk ? null : NamedDelegate(parameter.type) };
    }

    internal static bool Strong(Evidence? evidence) => evidence is { level: EvidenceLevel.Declared or EvidenceLevel.UserAsserted or EvidenceLevel.Verified }
        && !string.IsNullOrWhiteSpace(evidence.origin);

    internal string Carrier(StableId id) => carriers.TryGetValue(id, out string? carrier) ? carrier : throw new InvalidOperationException("A callback carrier is absent from the frozen ABI plan.");
    internal InteropType Resolve(StableId id)
    {
        InteropType type = module.GetType(id);
        HashSet<StableId> seen = [];
        while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
        {
            if (!seen.Add(type.id) || type.elementType is not StableId element) throw new InvalidOperationException("A callback type contains an unknown or cyclic underlying representation.");
            type = module.GetType(element);
        }
        return type;
    }
    internal bool VoidPointer(StableId id) => Resolve(id) is { category: NativeTypeCategory.Pointer, elementType: StableId element }
        && Resolve(element) is { category: NativeTypeCategory.Primitive, primitiveKind: "Void" };
    internal bool Scalar(
        StableId id,
        bool allowVoid
    ) {
        InteropType type = Resolve(id);
        if (type.category == NativeTypeCategory.Enumeration && type.elementType is StableId element) return Scalar(element, false);
        return type.category == NativeTypeCategory.Primitive && (allowVoid && type.primitiveKind == "Void"
            || Carrier(id) is "byte" or "sbyte" or "short" or "ushort" or "int" or "uint" or "long" or "ulong" or "nint" or "nuint" or "float" or "double");
    }
    internal bool Channel(
        StableId id,
        bool allowVoid
    ) => Scalar(id, allowVoid) || Resolve(id).category == NativeTypeCategory.Pointer;
    private string? NamedDelegate(StableId id)
    {
        HashSet<StableId> seen = [];
        while (seen.Add(id))
        {
            CSharpAbiDelegatePlan? declaration = plan.abiDelegates.SingleOrDefault(declaration => declaration.nativeType == id);
            if (declaration is not null) return CSharpGeneratedTypeScope.QualifiedReference(plan.api, declaration.name);
            InteropType type = module.GetType(id);
            if (type.category is not (NativeTypeCategory.Alias or NativeTypeCategory.Qualified or NativeTypeCategory.Pointer) || type.elementType is not StableId element) return null;
            id = element;
        }
        return null;
    }
    private string Fallback(
        CallbackContract contract,
        StableId resultId,
        string carrier
    ) {
        InteropType type = Resolve(resultId);
        if (type.primitiveKind == "Void")
        {
            if (contract.failureReturn is not null || contract.failureReturnsNull is not null) throw new NotSupportedException("A void callback cannot declare a native result fallback.");
            return string.Empty;
        }
        if (type.category == NativeTypeCategory.Pointer)
        {
            if (contract.failureReturnsNull != true || contract.failureReturn is not null) throw new NotSupportedException("A pointer callback requires an explicit null failure result.");
            return carrier == "nint" ? "(nint)0" : "null";
        }
        while (type.category == NativeTypeCategory.Enumeration && type.elementType is StableId element) type = Resolve(element);
        NativeScalarConstant value = contract.failureReturn ?? throw new NotSupportedException("A nonvoid callback requires an explicit native failure return value.");
        if (contract.failureReturnsNull is not null) throw new NotSupportedException("A scalar callback result cannot also declare a null-pointer fallback.");
        return ScalarExpression(value, resultId, carrier);
    }

    internal string ScalarExpression(
        NativeScalarConstant value,
        StableId resultId,
        string carrier
    ) {
        InteropType type = Resolve(resultId);
        while (type.category == NativeTypeCategory.Enumeration && type.elementType is StableId element) type = Resolve(element);
        if (value.evidence is null || value.evidence.level is not (EvidenceLevel.Declared or EvidenceLevel.UserAsserted or EvidenceLevel.Verified)
            || string.IsNullOrWhiteSpace(value.evidence.origin) || value.width != type.size)
            throw new NotSupportedException("A callback fallback must retain evidence and its exact native result representation.");
        string expression;
        if (type.primitiveKind == "Bool" && value.kind == NativeScalarConstantKind.Boolean && value.value is "0" or "1" && value.isSigned is null)
            expression = value.value;
        else if (type.primitiveKind == "Float" && value.kind == NativeScalarConstantKind.Binary32 && value.width == 4 && value.isSigned is null
            && Regex.IsMatch(value.value, "^[0-9A-F]{8}$", RegexOptions.CultureInvariant))
            expression = "global::System.BitConverter.UInt32BitsToSingle(0x" + value.value + "U)";
        else if (type.primitiveKind == "Double" && value.kind == NativeScalarConstantKind.Binary64 && value.width == 8 && value.isSigned is null
            && Regex.IsMatch(value.value, "^[0-9A-F]{16}$", RegexOptions.CultureInvariant))
            expression = "global::System.BitConverter.UInt64BitsToDouble(0x" + value.value + "UL)";
        else if (value.kind == NativeScalarConstantKind.Integer && value.isSigned is bool signed && signed == type.isSigned && value.width is 1 or 2 or 4 or 8
            && Regex.IsMatch(value.value, "^-?(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant)
            && BigInteger.TryParse(value.value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out BigInteger integral))
        {
            BigInteger limit = BigInteger.One << (value.width * 8 - (signed ? 1 : 0));
            if (integral < (signed ? -limit : BigInteger.Zero) || integral >= limit) throw new NotSupportedException("The callback fallback exceeds its exact native result width.");
            expression = value.value + (value.width == 8 ? signed ? "L" : "UL" : value.width == 4 && !signed ? "U" : "");
        }
        else throw new NotSupportedException("The declared callback failure result does not match its native scalar category.");
        return "(" + carrier + ")(" + expression + ")";
    }


    private static string Convention(string? convention) => convention switch
    {
        "C" or "Cdecl" => "Cdecl",
        "X86StdCall" or "StdCall" or "Stdcall" => "Stdcall",
        _ => throw new NotSupportedException("The managed callback projection requires an implemented Cdecl or Stdcall reverse-call convention.")
    };
}
