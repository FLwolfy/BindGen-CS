using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;

namespace BGCS.Configuration;

internal static class CallbackConfigurationBinder
{
    internal static ImmutableArray<InteropFunction> Apply(
        InteropModule module,
        ImmutableArray<InteropFunction> functions,
        ImmutableArray<CallbackParameterConfiguration> rules,
        string origin,
        Func<string, InteropFunction> select
    ) {
        foreach (CallbackParameterConfiguration rule in rules)
        {
            InteropFunction selected = select(rule.function);
            int functionIndex = Array.FindIndex(functions.ToArray(), function => function.id == selected.id);
            InteropFunction function = functions[functionIndex];
            InteropParameter[] parameters = function.parameters.Where(parameter => parameter.name == rule.parameter || parameter.sourceName == rule.parameter).ToArray();
            if (parameters.Length != 1) throw new ArgumentException($"Callback '{rule.function}:{rule.parameter}' must identify exactly one native parameter.");
            InteropParameter parameter = parameters[0];
            InteropType callback = Resolve(parameter.type);
            if (callback.category == NativeTypeCategory.Pointer && callback.elementType is StableId pointee) callback = Resolve(pointee);
            if (callback.category != NativeTypeCategory.Function) throw new ArgumentException("A callback contract requires an exact native function-pointer type.");
            StableId? userData = null;
            if (rule.userDataParameter is not null)
            {
                InteropParameter[] related = function.parameters.Where(candidate => candidate.name == rule.userDataParameter || candidate.sourceName == rule.userDataParameter).ToArray();
                if (related.Length != 1 || related[0].id == parameter.id) throw new ArgumentException("A callback user-data rule must identify a separate native parameter.");
                userData = related[0].id;
            }
            Evidence evidence = new(EvidenceLevel.UserAsserted, origin + ":semantics:callback:" + rule.function + ":" + rule.parameter);
            NativeScalarConstant? fallback = null;
            bool? pointerFallback = null;
            if (rule.failureReturn is not null)
            {
                InteropType result = Resolve(callback.returnType ?? throw new ArgumentException("The callback has no result type."));
                if (result.category == NativeTypeCategory.Pointer)
                {
                    if (rule.failureReturn != "null") throw new ArgumentException("A native callback pointer fallback must explicitly be null.");
                    pointerFallback = true;
                }
                else fallback = ScalarFallback(result, rule.failureReturn, evidence);
            }
            CallbackContract contract = new(callback.id, new(rule.lifetime, null, evidence), evidence)
            {
                retained = rule.retained,
                threading = rule.threading,
                userDataParameter = userData,
                callableUserDataIndex = rule.callableUserDataIndex,
                requiredResultOutputIndex = parameter.callback?.requiredResultOutputIndex,
                failureReturn = fallback,
                failureReturnsNull = pointerFallback,
                unregisterFunction = rule.unregisterFunction is null ? null : select(rule.unregisterFunction).id,
                unregisterQuiescence = rule.unregisterQuiescence
            };
            if (rule.registration is { } registration)
            {
                InteropFunction unregister = select(rule.unregisterFunction ?? throw new ArgumentException("A returned-handle callback registration requires an exact native unregister operation."));
                StableId? Parameter(string? selector)
                {
                    if (selector is null) return null;
                    InteropParameter[] matches = unregister.parameters.Where(candidate => candidate.name == selector || candidate.sourceName == selector).ToArray();
                    return matches.Length == 1 ? matches[0].id : throw new ArgumentException("A callback unregister input must identify exactly one native parameter.");
                }
                contract = contract with
                {
                    registration = new(registration.kind == NativeCallbackRegistrationKind.ReturnedHandle ? Parameter(registration.unregisterHandleParameter) : null,
                    registration.nullResultQuiescence, registration.releaseThreadDomain, evidence)
                    {
                        kind = registration.kind,
                        unregisterCallbackParameter = Parameter(registration.unregisterCallbackParameter),
                        unregisterUserDataParameter = Parameter(registration.unregisterUserDataParameter),
                        successfulReturn = registration.successfulReturn is null ? null : ScalarFallback(Resolve(function.returnType), registration.successfulReturn, evidence),
                        failureResultQuiescence = registration.failureResultQuiescence,
                        unregisterIsNonBlocking = registration.unregisterIsNonBlocking
                    }
                };
            }
            functions = functions.SetItem(functionIndex, function with
            { parameters = function.parameters.SetItem(function.parameters.IndexOf(parameter), parameter with { callback = contract }) });
        }
        return functions;

        InteropType Resolve(StableId id)
        {
            HashSet<StableId> seen = [];
            InteropType type = module.GetType(id);
            while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified or NativeTypeCategory.Enumeration)
            {
                if (!seen.Add(type.id) || type.elementType is not StableId element) throw new ArgumentException("A callback semantic type has a missing or cyclic underlying representation.");
                type = module.GetType(element);
            }
            return type;
        }
    }

    internal static NativeScalarConstant ScalarFallback(
        InteropType result,
        string value,
        Evidence evidence
    ) {
        if (result.category != NativeTypeCategory.Primitive || result.size is not (1 or 2 or 4 or 8))
            throw new ArgumentException("A callback fallback requires an exact supported scalar representation or an explicit null pointer.");
        int width = checked((int)result.size.Value);
        if (result.primitiveKind == "Bool")
        {
            if (value is not ("0" or "1")) throw new ArgumentException("A native Boolean callback fallback must be 0 or 1.");
            return new(NativeScalarConstantKind.Boolean, value, width, null, evidence);
        }
        if (result.primitiveKind is "Float" or "Double")
        {
            if (width == 4 && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float single) && float.IsFinite(single))
                return new(NativeScalarConstantKind.Binary32, BitConverter.SingleToUInt32Bits(single).ToString("X8", CultureInfo.InvariantCulture), 4, null, evidence);
            if (width == 8 && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double real) && double.IsFinite(real))
                return new(NativeScalarConstantKind.Binary64, BitConverter.DoubleToUInt64Bits(real).ToString("X16", CultureInfo.InvariantCulture), 8, null, evidence);
            throw new ArgumentException("A floating callback fallback requires finite invariant scalar data matching its native width.");
        }
        if (result.isSigned is not bool signed || !Regex.IsMatch(value, "^-?(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant)
            || !BigInteger.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out BigInteger integer))
            throw new ArgumentException("An integral callback fallback requires canonical decimal data and known native signedness.");
        BigInteger limit = BigInteger.One << (width * 8 - (signed ? 1 : 0));
        if (integer < (signed ? -limit : BigInteger.Zero) || integer >= limit) throw new ArgumentException("A callback fallback exceeds its exact native result representation.");
        return new(NativeScalarConstantKind.Integer, value, width, signed, evidence);
    }
}
