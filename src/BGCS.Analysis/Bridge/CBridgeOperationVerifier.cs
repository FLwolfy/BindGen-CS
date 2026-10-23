using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BGCS.Core.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Native;

namespace BGCS.Analysis.Bridge;

/// <summary>Validates typed C bridge operations and their declared source contributions before emission or semantic merge.</summary>
public sealed class CBridgeOperationVerifier
{
    /// <summary>Checks complete C input mappings, conversions, source identities and finite callback contexts.</summary>
    /// <param name="plan">The detached native C bridge plan.</param>
    public void Verify(NativeBridgePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.source.language != SourceLanguage.C) throw new ArgumentException("A C bridge verifier requires a C source graph.", nameof(plan));
        Dictionary<StableId, BridgeType> types = plan.types.ToDictionary(type => type.id);
        if (plan.exports.Select(export => export.id).Distinct().Count() != plan.exports.Length
            || plan.exports.Select(export => export.name).Distinct(StringComparer.Ordinal).Count() != plan.exports.Length)
            throw new InvalidOperationException("C bridge exports have conflicting identities or symbols.");
        ControlledSourceFragmentVerifier fragments = new();
        foreach (ControlledSourceFragment fragment in plan.sourceFragments)
        {
            if (fragment.kind != SourceFragmentKind.TranslationUnit) throw new InvalidOperationException("A C helper must use translation-unit source placement.");
            fragments.Validate(fragment, plan.source, plan.headers, []);
        }
        foreach (BridgeType type in plan.types)
        {
            if (!Enum.IsDefined(type.kind)) throw new InvalidOperationException("A C carrier has an undefined kind.");
            if (type.kind == BridgeTypeKind.NativeStorage) plan.source.GetType(type.nativeType ?? throw new InvalidOperationException("Native C storage omitted its source identity."));
            else if (type.kind == BridgeTypeKind.Pointer) _ = types[type.elementType ?? throw new InvalidOperationException("A C pointer omitted its pointee.")];
            else if (type.kind == BridgeTypeKind.Function)
            {
                if (!type.hasPrototype || type.isVariadic || type.parameterTypes.IsDefault || type.callingConvention is not ("C" or "Default"))
                    throw new InvalidOperationException("A normalized C callback requires a complete fixed C prototype.");
                _ = types[type.returnType ?? throw new InvalidOperationException("A C callback omitted its result.")];
                foreach (StableId parameter in type.parameterTypes) _ = types[parameter];
            }
            else if (type.kind is not (BridgeTypeKind.Scalar or BridgeTypeKind.Enumeration or BridgeTypeKind.Handle))
                throw new NotSupportedException("This C representation requires an explicit C storage implementation; use native storage, scalar, pointer or callable lowering.");
            if (type.kind is BridgeTypeKind.Function or BridgeTypeKind.Handle or BridgeTypeKind.Enumeration) Identifier(type.cName);
            if (type.kind == BridgeTypeKind.Scalar && !Regex.IsMatch(type.cName, "^(void|float|double|u?int(8|16|32|64)_t|u?intptr_t|size_t|ptrdiff_t)$", RegexOptions.CultureInvariant))
                throw new NotSupportedException("A C scalar carrier must use an implemented fixed scalar spelling.");
        }
        foreach (AggregateCallbackAdapter adapter in plan.aggregateCallbacks)
        {
            NativeDeclaration registration = plan.source.GetDeclaration(adapter.sourceFunction);
            NativeDeclaration parameter = plan.source.GetDeclaration(adapter.sourceParameter);
            NativeType signature = StandardLibraryFactReader.Resolve(plan.source, adapter.sourceCallableType);
            BridgeType carrier = types[adapter.carrierType];
            if (!registration.children.Contains(parameter.id) || signature.category != NativeTypeCategory.Function
                || signature.returnType is not StableId result || !signature.hasPrototype || signature.isVariadic
                || signature.callingConvention is not ("C" or "Default") || carrier.kind != BridgeTypeKind.Function)
                throw new InvalidOperationException("A C callback adapter lost its original fixed callable identity.");
            NativeType original = StandardLibraryFactReader.Resolve(plan.source, parameter.type!.Value);
            if (original.category == NativeTypeCategory.Pointer) original = StandardLibraryFactReader.Resolve(plan.source, original.elementType!.Value);
            bool resultOut = StandardLibraryFactReader.Resolve(plan.source, result).category == NativeTypeCategory.Record;
            if (original.id != signature.id || carrier.parameterTypes.Length != signature.parameterTypes.Length + (resultOut ? 1 : 0)
                || carrier.returnType is not StableId returned || !(resultOut ? IsVoidStorage(returned) : Identity(returned, result, false)))
                throw new InvalidOperationException("A normalized C callback changed its exact result or parameter channels.");
            for (int index = 0; index < signature.parameterTypes.Length; index++)
                if (!Identity(carrier.parameterTypes[index], signature.parameterTypes[index],
                    StandardLibraryFactReader.Resolve(plan.source, signature.parameterTypes[index]).category == NativeTypeCategory.Record))
                    throw new InvalidOperationException("A normalized C callback parameter changed its source storage or pointer direction.");
            if (signature.parameterTypes.Select((
                native,
                index
            ) => (native, index)).Any(item => StandardLibraryFactReader.Resolve(plan.source, item.native).category == NativeTypeCategory.Record
                && !types[carrier.parameterTypes[item.index]].isConst))
                throw new InvalidOperationException("A normalized callback input aggregate lost its borrowed const storage channel.");
            if (resultOut && (!Identity(carrier.parameterTypes[^1], result, true) || types[carrier.parameterTypes[^1]].isConst))
                throw new InvalidOperationException("A normalized C callback lost its writable complete result channel.");
        }
        foreach (NativeCallbackContextPlan context in plan.callbackContexts)
        {
            AggregateCallbackAdapter[] callbacks = plan.aggregateCallbacks.Where(adapter => adapter.sourceFunction == context.registrationFunction).ToArray();
            if (callbacks.Length == 0 || context.callbackParameters.IsDefaultOrEmpty
                || !context.callbackParameters.OrderBy(id => id.value, StringComparer.Ordinal).SequenceEqual(callbacks.Select(adapter => adapter.sourceParameter).OrderBy(id => id.value, StringComparer.Ordinal))
                || callbacks.Select(adapter => adapter.userDataParameter).Distinct().Count() != 1
                || types[context.carrierType].kind != BridgeTypeKind.Handle
                || plan.source.GetDeclaration(context.registrationFunction).returnType != context.nativeHandleType
                || StandardLibraryFactReader.Resolve(plan.source, context.nativeHandleType).category != NativeTypeCategory.Pointer)
                throw new InvalidOperationException("A retained C callback context lost its exact registration and handle association.");
            foreach (AggregateCallbackAdapter adapter in callbacks)
                if (adapter.contract.retained != true || adapter.contract.unregisterFunction != context.unregisterFunction
                    || adapter.contract.unregisterQuiescence != BGCS.Core.Interop.NativeQuiescenceKind.NoActiveNativeInvocations
                    || adapter.contract.registration is not
                    {
                        kind: BGCS.Core.Interop.NativeCallbackRegistrationKind.ReturnedHandle,
                        nullResultQuiescence: BGCS.Core.Interop.NativeQuiescenceKind.NoActiveNativeInvocations
                    } registration
                    || registration.unregisterHandleParameter != context.unregisterHandleParameter)
                    throw new InvalidOperationException("A retained C context requires an explicit full native-frame stop-and-join protocol.");
            if (!plan.source.GetDeclaration(context.unregisterFunction).children.Contains(context.unregisterHandleParameter)
                || context.cancelFunction == context.unregisterFunction
                || context.cancelFunction is StableId cancel && (context.cancellationHandleParameter is not StableId parameter
                    || !plan.source.GetDeclaration(cancel).children.Contains(parameter)))
                throw new InvalidOperationException("A retained C context has a mismatched stop or cancellation handle.");
        }
        bool IsVoidStorage(StableId id) => types[id].kind == BridgeTypeKind.NativeStorage && types[id].nativeType is StableId native
            && StandardLibraryFactReader.Resolve(plan.source, native).primitiveKind == "Void";
        bool Identity(
            StableId carrier,
            StableId native,
            bool pointer
        ) {
            BridgeType type = types[carrier];
            if (pointer)
            {
                if (type.kind != BridgeTypeKind.Pointer || type.elementType is not StableId element) return false;
                type = types[element];
            }
            return type.kind == BridgeTypeKind.NativeStorage && type.nativeType is StableId source
                && StandardLibraryFactReader.Resolve(plan.source, source).id == StandardLibraryFactReader.Resolve(plan.source, native).id;
        }
        foreach (BridgeExport export in plan.exports)
        {
            Identifier(export.name);
            if (export.operation.kind is not (BridgeOperationKind.FreeCall or BridgeOperationKind.FiniteVariadicCall) || export.operation.declaration is not StableId declaration
                || export.operation.ownerType is not null || export.operation.instanceParameter is not null || export.usesErrorChannel
                || export.operation.castTarget is not null || export.operation.standardValue is not null)
                throw new NotSupportedException("A C callable extension must describe a fixed free call without an implicit C++ error channel.");
            if (export.resultOwnership is not null || export.resultRelease is not null || export.resultBorrowsInstance || export.inputAdapterOf is not null)
                throw new NotSupportedException("An owned C result or input adapter requires an explicitly implemented release-aware C semantic protocol; use declared source ownership for an unchanged raw pointer result.");
            NativeDeclaration callable = plan.source.GetDeclaration(declaration);
            if (callable.kind != "Function" || !callable.hasPrototype || callable.isVariadic != (export.operation.kind == BridgeOperationKind.FiniteVariadicCall))
                throw new InvalidOperationException("A C bridge omitted its original callable or exact finite adapter kind.");
            NativeDeclaration[] original = callable.children.Select(plan.source.GetDeclaration).Where(item => item.kind == "Parameter").ToArray();
            NativeVariadicAdapter? finite = export.operation.finiteVariadic;
            if (export.operation.kind == BridgeOperationKind.FiniteVariadicCall)
            {
                if (finite is null || finite.sourceDeclaration != callable.id || finite.fixedParameterCount != original.Length
                    || finite.argumentTypes.IsDefault || finite.additionalArguments.IsDefault || finite.argumentTypes.Length != finite.additionalArguments.Length
                    || finite.sourceSignature != new BGCS.Analysis.Types.NativeTypeFormatter().CallableSignature(plan.source, callable)
                    || !export.operation.argumentGroups.IsEmpty)
                    throw new InvalidOperationException("A finite C operation lost its exact original signature, real argument type facts or flat ABI order.");
                Identifier(finite.wrapper);
            }
            else if (finite is not null) throw new InvalidOperationException("A fixed C operation cannot carry a variadic adapter.");
            StableId[] inputTypes = original.Select(parameter => parameter.type!.Value).Concat(finite?.argumentTypes ?? []).ToArray();
            if (export.parameters.IsDefault || export.parameters.Select(parameter => parameter.id).Distinct().Count() != export.parameters.Length
                || export.parameters.Select(parameter => parameter.name).Append("bgcs_result").Distinct(StringComparer.Ordinal).Count() != export.parameters.Length + 1)
                throw new InvalidOperationException("C bridge parameters have duplicate or reserved identities.");
            Dictionary<StableId, BridgeParameter> parameters = export.parameters.ToDictionary(parameter => parameter.id);
            HashSet<StableId> expanded = [];
            if (export.operation.argumentGroups.IsDefault) throw new InvalidOperationException("C argument groups must be initialized.");
            if (export.operation.argumentGroups.IsEmpty)
            {
                if (export.parameters.Length != inputTypes.Length) throw new InvalidOperationException("A C callable changed arity without explicit argument groups or finite argument facts.");
                for (int index = 0; index < inputTypes.Length; index++)
                    if (export.parameters[index].nativeType != inputTypes[index]) throw new InvalidOperationException("A C carrier changed its original parameter identity.");
            }
            else
            {
                if (export.operation.argumentGroups.Length != original.Length) throw new InvalidOperationException("C argument groups changed original native arity.");
                HashSet<StableId> covered = [];
                for (int index = 0; index < original.Length; index++)
                {
                    BridgeArgumentGroup group = export.operation.argumentGroups[index];
                    if (group.sourceParameter != original[index].id || group.nativeType != original[index].type || group.inputs.IsDefaultOrEmpty)
                        throw new InvalidOperationException("A C argument group lost its exact original parameter.");
                    HashSet<string> names = new(StringComparer.Ordinal);
                    foreach (BridgeArgumentBinding binding in group.inputs)
                    {
                        if (!Regex.IsMatch(binding.name, "^[A-Za-z][A-Za-z0-9]*$", RegexOptions.CultureInvariant) || binding.name == "nativeType"
                            || !names.Add(binding.name) || !parameters.TryGetValue(binding.parameter, out BridgeParameter? parameter)
                            || !covered.Add(binding.parameter) || parameter.nativeType != group.nativeType)
                            throw new InvalidOperationException("C argument groups contain duplicate, missing or mismatched carriers.");
                        if (group.expression is not null) expanded.Add(binding.parameter);
                    }
                    if (group.standardSequence is not null) throw new NotSupportedException("C sequences require an explicit C argument expression rather than a C++ standard-library construction.");
                    if (group.expression is not null) Expression(group.expression, names.Append("nativeType"));
                    else if (group.inputs.Length != 1) throw new InvalidOperationException("Multiple C carriers require one explicit argument expression.");
                }
                if (covered.Count != parameters.Count) throw new InvalidOperationException("C argument groups omitted an exported carrier.");
            }
            foreach (BridgeParameter parameter in export.parameters)
            {
                Identifier(parameter.name);
                _ = types[parameter.type];
                if (parameter.nativeType is not StableId originalType || parameter.generatedType is not null)
                    throw new InvalidOperationException("A C value carrier must retain its actual original native type.");
                _ = plan.source.GetType(originalType);
                if (!Enum.IsDefined(parameter.conversion)) throw new InvalidOperationException("A C argument has an undefined conversion.");
                if (expanded.Contains(parameter.id))
                {
                    if (parameter.conversionExpression is not null || parameter.managedProjection is not null)
                        throw new InvalidOperationException("Grouped C carriers cannot carry independent value-conversion expressions.");
                }
                else if (parameter.conversion == BridgeConversionKind.SourceExpression)
                    Expression(parameter.conversionExpression ?? throw new InvalidOperationException("A C input expression is absent."), ["value", "nativeType"]);
                else if (parameter.conversion is not (BridgeConversionKind.ScalarCast or BridgeConversionKind.PointerCast or BridgeConversionKind.Dereference)
                    || parameter.conversionExpression is not null)
                    throw new NotSupportedException("This C input conversion requires an explicit typed callable expression.");
            }
            if (export.operation.invocationExpression is ControlledSourceFragment invocation)
            {
                if (invocation.text.Split("{invocation}", StringSplitOptions.None).Length != 2) throw new InvalidOperationException("A C invocation wrapper must consume the planned call exactly once.");
                Expression(invocation, ["invocation"]);
            }
            if (export.resultType is StableId result)
            {
                _ = types[result];
                if (export.nativeResultType != callable.returnType) throw new InvalidOperationException("A C result lost its original type identity.");
                if (export.resultConversion == BridgeConversionKind.SourceExpression)
                    Expression(export.resultExpression ?? throw new InvalidOperationException("A C result expression is absent."), ["value", "resultType"]);
                else if (export.resultConversion is not (BridgeConversionKind.ScalarCast or BridgeConversionKind.PointerCast)
                    || export.resultExpression is not null) throw new NotSupportedException("This C result conversion requires an explicit typed callable expression.");
            }
            else if (callable.returnType is not StableId nothing || StandardLibraryFactReader.Resolve(plan.source, nothing).primitiveKind != "Void")
                throw new InvalidOperationException("A C bridge discarded its original nonvoid result.");
        }
        void Expression(
            ControlledSourceFragment fragment,
            System.Collections.Generic.IEnumerable<string> placeholders
        ) {
            if (fragment.kind != SourceFragmentKind.Expression) throw new InvalidOperationException("A C conversion must be an expression fragment.");
            fragments.Validate(fragment, plan.source, plan.headers, placeholders);
        }
    }
    private static void Identifier(string value)
    {
        if (!Regex.IsMatch(value, "^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.CultureInvariant) || value.Contains("__", StringComparison.Ordinal))
            throw new InvalidOperationException("A C bridge contains an invalid or reserved allocated identifier.");
    }
}
