using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using BGCS.Analysis.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;

namespace BGCS.CSharp.Planning;

/// <summary>Plans a finite managed virtual callback interface from independently checked C ABI identities.</summary>
/// <param name="contract">The neutral checked proxy protocol, including unknown caller retention.</param>
/// <param name="name">The stable portable managed suffix.</param>
/// <param name="interfaceName">The nested managed callback interface.</param>
/// <param name="registrationName">The explicitly caller-controlled registration type.</param>
/// <param name="interfacePointerType">The exact borrowed native interface carrier.</param>
/// <param name="create">The checked native construction and error cleanup.</param>
/// <param name="destroy">The checked native destruction and error cleanup.</param>
/// <param name="cast">The real native base adjustment and error cleanup.</param>
/// <param name="userDataIndex">The exact factory user-data argument.</param>
/// <param name="slots">The independently checked Cdecl callback slots.</param>
public sealed record CSharpVirtualProxyPlan(
    VirtualProxyContract contract,
    string name,
    string interfaceName,
    string registrationName,
    string interfacePointerType,
    CSharpVirtualOperationPlan create,
    CSharpVirtualOperationPlan destroy,
    CSharpVirtualOperationPlan cast,
    int userDataIndex,
    ImmutableArray<CSharpVirtualSlotPlan> slots
);

/// <summary>Plans a fixed status operation and owned native error channel without inferring cleanup names.</summary>
/// <param name="methodIndex">The exact imported operation.</param>
/// <param name="instanceIndex">The native receiver, or minus one for construction.</param>
/// <param name="resultIndex">The exact result-out channel, or minus one for void.</param>
/// <param name="resultType">The exact result storage carrier.</param>
/// <param name="errorIndex">The exact owned error-out channel.</param>
/// <param name="errorType">The exact native error pointer carrier.</param>
/// <param name="errorReleaseIndex">The matching void error cleanup import.</param>
/// <param name="successValue">The exact successful native status value.</param>
public sealed record CSharpVirtualOperationPlan(
    int methodIndex,
    int instanceIndex,
    int resultIndex,
    string resultType,
    int errorIndex,
    string errorType,
    int errorReleaseIndex,
    int successValue
);

/// <summary>Plans one static AOT thunk and its managed interface member.</summary>
/// <param name="contract">The checked neutral slot.</param>
/// <param name="name">The checked managed overload name.</param>
/// <param name="nativeParameterTypes">The exact C callback arguments, including user data and result storage.</param>
/// <param name="factoryParameterIndex">The exact native factory input receiving this thunk address.</param>
/// <param name="parameters">Managed input conversions from the typed native carriers.</param>
/// <param name="result">The managed result conversion, or null for void.</param>
public sealed record CSharpVirtualSlotPlan(
    VirtualProxySlotContract contract,
    string name,
    ImmutableArray<string> nativeParameterTypes,
    int factoryParameterIndex,
    ImmutableArray<CSharpVirtualValuePlan> parameters,
    CSharpVirtualValuePlan? result
);

/// <summary>Plans one finite callback scalar or explicitly borrowed opaque address.</summary>
/// <param name="contract">The original checked channel.</param>
/// <param name="nativeType">The exact C value carrier.</param>
/// <param name="managedType">The managed scalar or borrowed address representation.</param>
/// <param name="isBoolean">Whether the original C++ Boolean uses its checked uint8 callback carrier.</param>
/// <param name="isPointer">Whether this is an explicitly borrowed address rather than an owning wrapper.</param>
public sealed record CSharpVirtualValuePlan(
    VirtualProxyValueContract contract,
    string nativeType,
    string managedType,
    bool isBoolean,
    bool isPointer
);

/// <summary>Explains why a correct raw virtual ABI has no optional managed registration projection.</summary>
/// <param name="proxy">The exact neutral proxy identity.</param>
/// <param name="source">The original interface location.</param>
/// <param name="reason">The missing managed lifetime or representation capability.</param>
public sealed record CSharpVirtualProxyOmissionPlan(
    StableId proxy,
    SourceLocation? source,
    string reason
);

internal static class VirtualProxyPlanner
{
    internal static (ImmutableArray<CSharpVirtualProxyPlan> Plans, ImmutableArray<CSharpVirtualProxyOmissionPlan> Omissions) Plan(CSharpEmissionPlan plan)
    {
        InteropModule module = plan.api.interop;
        new VirtualProxyProtocolVerifier().Verify(module);
        if (module.virtualProxies.IsEmpty) return ([], []);
        Dictionary<StableId, int> imports = plan.methods.Select((
            method,
            index
        ) => (method.nativeExport, index)).ToDictionary(pair => pair.nativeExport, pair => pair.index);
        Dictionary<StableId, string> carriers = plan.types.ToDictionary(type => type.nativeType, type => type.syntax);
        HashSet<string> names = plan.storage.Select(type => type.name.TrimStart('@')).Concat(plan.managedEnums.Select(type => type.name.TrimStart('@')))
            .Concat(plan.objects.Select(type => type.name.TrimStart('@'))).Append(plan.contextName.TrimStart('@')).Append(plan.api.name.TrimStart('@'))
            .ToHashSet(StringComparer.Ordinal);
        ImmutableArray<CSharpVirtualProxyPlan>.Builder result = ImmutableArray.CreateBuilder<CSharpVirtualProxyPlan>();
        ImmutableArray<CSharpVirtualProxyOmissionPlan>.Builder omissions = ImmutableArray.CreateBuilder<CSharpVirtualProxyOmissionPlan>();
        foreach (VirtualProxyContract proxy in module.virtualProxies.OrderBy(proxy => proxy.id.value, StringComparer.Ordinal))
        {
            try
            {
                if (plan.managedRuntime is { allowsBlockingDisposal: false })
                    throw new NotSupportedException("A managed virtual proxy requires native full-frame stop/join and synchronous draining; this runtime needs a separate nonblocking virtual-proxy protocol. The exact raw proxy ABI remains available.");
                if (proxy.ownership.releaseThreadDomain != "any")
                    throw new NotSupportedException("A managed virtual proxy needs an explicit cleanup protocol that is valid on the closing thread.");
                HashSet<string> candidateNames = new(names, StringComparer.Ordinal);
                string suffix = Regex.Replace(proxy.name, "[^A-Za-z0-9_]", "_", RegexOptions.CultureInvariant);
                suffix = CSharpNames.Identifier(char.ToUpperInvariant(suffix[0]) + suffix[1..]);
                string interfaceName = "I" + suffix + "Callbacks", registration = suffix + "CallbackRegistration";
                foreach (string reserved in new[] { interfaceName, registration, suffix + "NativeCall", "__Bgcs" + suffix + "CallbackState" })
                    if (!candidateNames.Add(reserved)) throw new ArgumentException("A managed virtual callback type conflicts with a declared type or another callback interface.");
                InteropFunction factory = Function(proxy.createFunction);
                CSharpVirtualOperationPlan create = Operation(proxy.createFunction, null, proxy.proxyResultParameter);
                CSharpVirtualOperationPlan destroy = Operation(proxy.destroyFunction, proxy.destroyInstanceParameter, null);
                CSharpVirtualOperationPlan cast = Operation(proxy.interfaceCastFunction, proxy.castInstanceParameter, proxy.interfaceResultParameter);
                ImmutableArray<CSharpVirtualSlotPlan>.Builder slots = ImmutableArray.CreateBuilder<CSharpVirtualSlotPlan>();
                HashSet<string> overloads = new(StringComparer.Ordinal);
                foreach (VirtualProxySlotContract slot in proxy.slots)
                {
                    InteropType callable = Resolve(slot.callableType);
                    if (callable.category != NativeTypeCategory.Function || callable.callingConvention is not ("C" or "Cdecl" or "Default")
                        || callable.returnType is not StableId status || Carrier(status) != "int" || callable.isVariadic || !callable.hasPrototype
                        || plan.opaqueCallables.Any(opaque => opaque.nativeType == callable.id))
                        throw new NotSupportedException("A generated virtual thunk requires a fixed checked Cdecl int32 status prototype.");
                    ImmutableArray<CSharpVirtualValuePlan> arguments = slot.parameters.Select(Value).ToImmutableArray();
                    CSharpVirtualValuePlan? output = slot.result is null ? null : Value(slot.result);
                    string name = CSharpNames.Identifier(slot.sourceName);
                    string key = name + "(" + string.Join(",", arguments.Select(argument => argument.managedType)) + ")";
                    if (name.TrimStart('@') == interfaceName || !overloads.Add(key))
                        throw new ArgumentException("Virtual callback source overloads collapse to the same managed signature; supply a neutral projection before C# emission.");
                    slots.Add(new(slot, name, callable.parameterTypes.Select(Carrier).ToImmutableArray(), Index(factory, slot.factoryParameter), arguments, output));
                }
                result.Add(new(proxy, suffix, interfaceName, registration, cast.resultType, create, destroy, cast,
                    Index(factory, proxy.userDataParameter), slots.ToImmutable()));
                names = candidateNames;
            }
            catch (Exception failure) when (failure is NotSupportedException or ArgumentException)
            { omissions.Add(new(proxy.id, proxy.source, failure.Message)); }
        }
        return (result.ToImmutable(), omissions.ToImmutable());

        InteropFunction Function(StableId id) => module.functions.Single(function => function.id == id);
        string Carrier(StableId id) => carriers.TryGetValue(id, out string? value) ? value : throw new InvalidOperationException("A virtual callback carrier is absent from the frozen C# type plan.");
        InteropType Resolve(StableId id)
        {
            InteropType type = module.GetType(id);
            while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified) type = module.GetType(type.elementType!.Value);
            return type;
        }
        CSharpVirtualValuePlan Value(VirtualProxyValueContract value)
        {
            InteropType native = Resolve(value.valueType);
            string carrier = Carrier(value.valueType);
            bool boolean = value.sourcePrimitiveKind == "Bool";
            if (boolean && (native.category != NativeTypeCategory.Primitive || native.size != 1 || carrier != "byte"))
                throw new NotSupportedException("A C++ Boolean callback requires its exact normalized uint8 carrier.");
            bool pointer = native.category == NativeTypeCategory.Pointer;
            if (!pointer && !boolean && native.category is not (NativeTypeCategory.Primitive or NativeTypeCategory.Enumeration))
                throw new NotSupportedException("A managed virtual callback channel needs a fixed scalar or an explicitly borrowed address.");
            if (native.primitiveKind == "Void") throw new NotSupportedException("Void is not a callback value channel.");
            return new(value, carrier, pointer ? "nint" : boolean ? "bool" : carrier, boolean, pointer);
        }
        CSharpVirtualOperationPlan Operation(
            StableId id,
            StableId? receiver,
            StableId? result
        ) {
            if (!imports.TryGetValue(id, out int index)) throw new NotSupportedException("A managed virtual proxy operation was omitted by the selected managed projection.");
            InteropFunction function = Function(id);
            CSharpMethodPlan method = plan.methods[index];
            ErrorContract error = function.error ?? throw new InvalidOperationException("A virtual proxy operation has no checked C exception boundary.");
            if (method.returnType != "int" || error.errorParameter is not StableId errorId
                || error.errorOwnership is not { kind: NativeOwnershipKind.Owned, releaseFunction: not null, releaseThreadDomain: "any" } ownership
                || !imports.TryGetValue(ownership.releaseFunction.Value, out int release)
                || !int.TryParse(error.successValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int success)
                || error.valueParameter != result || function.asynchronous is not null)
                throw new NotSupportedException("A virtual proxy operation needs a fixed int32 status, matching result and independently owned error cleanup.");
            int errorIndex = Index(function, errorId), resultIndex = result is StableId output ? Index(function, output) : -1;
            string errorType = Carrier(Resolve(function.parameters[errorIndex].type).elementType!.Value);
            CSharpMethodPlan cleanup = plan.methods[release];
            if (cleanup.returnType != "void" || cleanup.parameterTypes.Length != 1 || cleanup.parameterTypes[0] != errorType)
                throw new NotSupportedException("Virtual proxy error cleanup no longer accepts its exact native error pointer.");
            return new(index, receiver is StableId instance ? Index(function, instance) : -1, resultIndex,
                resultIndex < 0 ? "void" : Carrier(Resolve(function.parameters[resultIndex].type).elementType!.Value),
                errorIndex, errorType, release, success);
        }
        static int Index(
            InteropFunction function,
            StableId parameter
        ) {
            for (int index = 0; index < function.parameters.Length; index++) if (function.parameters[index].id == parameter) return index;
            throw new InvalidOperationException("A managed virtual callback refers to an absent exact native parameter identity.");
        }
    }
}
