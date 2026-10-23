using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BGCS.Analysis.Bridge;
using BGCS.Analysis.Types;
using BGCS.Core.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;

namespace BGCS.NativeBridge.Planning;

/// <summary>Adds explicitly selected finite sequence input representations to a preserved raw bridge.</summary>
public sealed class SequenceInputAdapterPlanner
{
    /// <summary>Plans checked scalar-element sequence copies or synchronous borrowed spans.</summary>
    /// <param name="module">The independently planned primary bridge.</param>
    /// <param name="requests">Additional input representations; primary exports remain unchanged.</param>
    /// <param name="cancellationToken">Cancels cooperative planning.</param>
    /// <returns>The original bridge plus the requested finite adapters.</returns>
    public NativeBridgePlan Apply(
        NativeBridgePlan module,
        ImmutableArray<SequenceInputAdapterRequest> requests,
        CancellationToken cancellationToken = default
    ) {
        ArgumentNullException.ThrowIfNull(module);
        if (requests.IsDefault) throw new ArgumentException("Sequence adapter requests must be initialized.", nameof(requests));
        new CppBridgeOperationVerifier().Verify(module);
        Dictionary<StableId, BridgeType> types = module.types.ToDictionary(type => type.id);
        List<BridgeExport> exports = module.exports.ToList();
        HashSet<StableId> adapterIds = [];
        NativeModule source = module.source;
        foreach (SequenceInputAdapterRequest request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request is null) throw new ArgumentException("A sequence adapter request cannot be null.", nameof(requests));
            ArgumentException.ThrowIfNullOrWhiteSpace(request.function);
            if (request.parameters.IsDefaultOrEmpty || request.parameters.Any(parameter => parameter is null || string.IsNullOrWhiteSpace(parameter.parameter)))
                throw new ArgumentException("A sequence adapter requires nonempty original parameter selectors.", nameof(requests));
            if (request.parameters.Select(parameter => parameter.parameter).Distinct(StringComparer.Ordinal).Count() != request.parameters.Length)
                throw new ArgumentException("A sequence adapter selected an original parameter more than once.", nameof(requests));
            BridgeExport[] candidates = module.exports.Where(export => export.inputAdapterOf is null && export.operation.declaration is not null).ToArray();
            BridgeExport[] matching = candidates.Where(export => export.name == request.function).ToArray();
            if (matching.Length == 0) matching = candidates.Where(export => new NativeTypeFormatter().CallableSignature(source, source.GetDeclaration(export.operation.declaration!.Value)) == request.function).ToArray();
            if (matching.Length == 0) matching = candidates.Where(export => source.GetDeclaration(export.operation.declaration!.Value).qualifiedName == request.function.TrimStart(':')).ToArray();
            if (matching.Length != 1) throw new InvalidOperationException("A sequence adapter must select one exact primary callable; use its sourceSignature or export name for overloads.");
            BridgeExport primary = matching[0];
            if (primary.operation.kind is not (BridgeOperationKind.FreeCall or BridgeOperationKind.StaticCall or BridgeOperationKind.InstanceCall))
                throw new NotSupportedException("Sequence adapters require ordinary calls; object construction needs its own escaping-argument contract.");
            if (primary.resultExpression is not null || primary.resultOwnership is not null || primary.operation.invocationExpression is not null)
                throw new NotSupportedException("A sequence adapter cannot infer the semantics of a custom invocation or result conversion.");
            if (!SequenceAdapterResultFacts.IsNonEscaping(module, primary))
                throw new NotSupportedException("A sequence adapter requires a scalar/void result or a fresh owned default-standard text value with its complete creating-call cleanup pair; borrowed references, pointers and custom/container results could escape temporary input storage.");
            NativeDeclaration callable = source.GetDeclaration(primary.operation.declaration!.Value);
            NativeDeclaration[] original = callable.children.Select(source.GetDeclaration).Where(declaration => declaration.kind == "Parameter").ToArray();
            Dictionary<StableId, SequenceInputParameterRequest> selected = [];
            foreach (SequenceInputParameterRequest parameter in request.parameters)
            {
                NativeDeclaration[] matches = original.Where(declaration => declaration.name == parameter.parameter).ToArray();
                if (matches.Length != 1) throw new InvalidOperationException("A sequence parameter selector does not identify one original native parameter.");
                selected.Add(matches[0].id, parameter);
            }
            StableId adapterId = new(primary.id.value + ":sequence:" + string.Join("|", selected.Keys.OrderBy(id => id.value, StringComparer.Ordinal).Select(id => id.value)));
            if (!adapterIds.Add(adapterId)) throw new InvalidOperationException("The same sequence input adapter was requested twice.");
            List<BridgeParameter> parameters = [];
            if (primary.operation.instanceParameter is StableId instance) parameters.Add(primary.parameters.Single(parameter => parameter.id == instance));
            List<BridgeArgumentGroup> groups = [];
            for (int index = 0; index < original.Length; index++)
            {
                NativeDeclaration parameter = original[index];
                BridgeArgumentGroup? existing = primary.operation.argumentGroups.SingleOrDefault(group => group.sourceParameter == parameter.id);
                if (!selected.TryGetValue(parameter.id, out SequenceInputParameterRequest? assertion))
                {
                    if (existing is null)
                    {
                        BridgeParameter input = primary.parameters.Where(input => input.id != primary.operation.instanceParameter).ElementAt(index);
                        parameters.Add(input);
                        groups.Add(new(parameter.id, parameter.type!.Value, [new("value", input.id)], null));
                    }
                    else
                    {
                        parameters.AddRange(existing.inputs.Select(input => primary.parameters.Single(carrier => carrier.id == input.parameter)));
                        groups.Add(existing);
                    }
                    continue;
                }
                if (existing is { expression: not null } || existing?.standardSequence is not null)
                    throw new NotSupportedException("A sequence input cannot replace an already configured native argument conversion.");
                NativeType shape = StandardLibraryFactReader.Resolve(source, parameter.type!.Value);
                bool reference = shape.category == NativeTypeCategory.Reference;
                if (reference)
                {
                    if (shape.isRValueReference || !HasConst(shape.elementType!.Value))
                        throw new NotSupportedException("A sequence reference adapter requires a const lvalue reference; mutable and rvalue object references remain raw.");
                    shape = StandardLibraryFactReader.Resolve(source, shape.elementType.Value);
                }
                StandardValueOperation facts = new StandardLibraryFactReader(module.familyBindings).Read(source, shape.id)
                    ?? throw new NotSupportedException("A sequence adapter requires a compiler-recognized standard-library specialization.");
                if (facts.family is not (StandardValueKind.String or StandardValueKind.Vector or StandardValueKind.Array or StandardValueKind.Span or StandardValueKind.Path))
                    throw new NotSupportedException("This native family has no finite sequence or standard filesystem UTF-8 input protocol.");
                StableId elementId = facts.family == StandardValueKind.Path ? StandardPathInputFacts.ReadElement(source, shape.id) : facts.argumentTypes[0];
                NativeType element = StandardLibraryFactReader.Resolve(source, elementId);
                if (element.category is not (NativeTypeCategory.Primitive or NativeTypeCategory.Enumeration) || element.primitiveKind == "Void")
                    throw new NotSupportedException("A standard sequence input requires checked scalar or enum element conversion.");
                StandardSequenceMaterialization materialization = facts.family == StandardValueKind.Span ? StandardSequenceMaterialization.BorrowedView : StandardSequenceMaterialization.CopyValue;
                CppFamilyBinding? binding = new StandardLibraryFactReader(module.familyBindings).FindBinding(source, shape.id);
                bool cStr = binding?.stringAccess == CppStringAccess.CStrNullTerminated;
                if (cStr && binding!.cStr!.constructorCopiesInput is null)
                    throw new NotSupportedException("A c_str input adapter requires an explicit constructorCopiesInput assertion; temporary terminated storage cannot be retained.");
                if (materialization == StandardSequenceMaterialization.CopyValue && HasConst(elementId))
                    throw new NotSupportedException("A copied sequence requires assignable scalar elements; const-element arrays need an explicit aggregate construction lowering.");
                bool requiresAssertion = reference || materialization == StandardSequenceMaterialization.BorrowedView || facts.family == StandardValueKind.Path;
                if (requiresAssertion && (assertion.retainsArgument != false || assertion.lifetime != NativeLifetimeKind.Call))
                    throw new NotSupportedException("Borrowed spans, temporary references and standard path inputs require explicit retainsArgument=false and lifetime=Call assertions.");
                if (!Enum.IsDefined(assertion.lifetime) || assertion.retainsArgument == true || assertion.lifetime is not (NativeLifetimeKind.Unknown or NativeLifetimeKind.Call))
                    throw new NotSupportedException("Sequence input adapters cannot satisfy retained or asynchronous native argument lifetimes.");
                if (materialization == StandardSequenceMaterialization.BorrowedView && element.category == NativeTypeCategory.Enumeration
                    || materialization == StandardSequenceMaterialization.BorrowedView && element.primitiveKind is "Bool" or "Char" or "WChar" or "Char8" or "Char16" or "Char32")
                    throw new NotSupportedException("A borrowed span requires identical native C element types; normalized bool, enum and distinct character carriers require copies.");
                ulong? extent = StandardSequenceArgumentVerifier.ReadExtent(source, shape.id, facts.family);
                BridgeType value = Carrier(element);
                bool readOnly = materialization == StandardSequenceMaterialization.CopyValue || HasConst(elementId);
                BridgeType pointer = new(new("bridge-pointer:" + (readOnly ? "const:" : string.Empty) + value.id.value), BridgeTypeKind.Pointer, string.Empty)
                { elementType = value.id, isConst = readOnly };
                types[pointer.id] = pointer;
                BridgeType length = Scalar("uint64_t");
                StableId dataId = new(adapterId.value + ":" + parameter.id.value + ":data");
                StableId lengthId = new(adapterId.value + ":" + parameter.id.value + ":length");
                parameters.Add(new(dataId, "arg" + index.ToString(CultureInfo.InvariantCulture) + "_data", pointer.id, parameter.type.Value, BridgeConversionKind.PointerCast));
                parameters.Add(new(lengthId, "arg" + index.ToString(CultureInfo.InvariantCulture) + "_length", length.id, parameter.type.Value, BridgeConversionKind.ScalarCast));
                Evidence evidence = cStr ? binding!.cStr!.constructorCopiesInput! : new(requiresAssertion ? EvidenceLevel.UserAsserted : EvidenceLevel.Declared,
                    requiresAssertion ? "sequence-input-assertion:" + new NativeTypeFormatter().CallableSignature(source, callable) + ":" + parameter.name : "standard-sequence-element-copy:" + shape.id.value);
                groups.Add(new(parameter.id, parameter.type.Value, [new("data", dataId), new("length", lengthId)], null)
                { standardSequence = new(facts.family, elementId, materialization, evidence) { extent = extent } });
            }
            string symbol = module.exportPrefix + "_sequence_" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(adapterId.value)))[..24];
            if (exports.Any(export => export.name == symbol)) throw new InvalidOperationException("A sequence adapter export name conflicts with an existing export.");
            exports.Add(primary with
            {
                id = adapterId,
                name = symbol,
                inputAdapterOf = primary.id,
                parameters = parameters.ToImmutableArray(),
                operation = primary.operation with { argumentGroups = groups.ToImmutableArray() }
            });
        }
        NativeBridgePlan resultModule = module with
        {
            types = types.Values.OrderBy(type => type.id.value, StringComparer.Ordinal).ToImmutableArray(),
            exports = exports.OrderBy(export => export.name, StringComparer.Ordinal).ToImmutableArray()
        };
        new CppBridgeOperationVerifier().Verify(resultModule);
        return resultModule;

        BridgeType Scalar(string name)
        {
            StableId id = new("bridge-scalar:" + name);
            if (!types.TryGetValue(id, out BridgeType? scalar)) types.Add(id, scalar = new(id, BridgeTypeKind.Scalar, name));
            return scalar;
        }
        BridgeType Carrier(NativeType type)
        {
            if (type.category == NativeTypeCategory.Enumeration)
                return types.Values.SingleOrDefault(carrier => carrier.kind == BridgeTypeKind.Enumeration && carrier.nativeType == type.id)
                    ?? throw new NotSupportedException("A sequence enum element has no checked bridge typedef.");
            string name = type.primitiveKind switch
            {
                "Bool" when type.size == 1 => "uint8_t",
                "Float" when type.size == 4 => "float",
                "Double" when type.size == 8 => "double",
                _ when type.size is 1 or 2 or 4 or 8 && type.isSigned is not null => (type.isSigned.Value ? "int" : "uint") + (type.size.Value * 8).ToString(CultureInfo.InvariantCulture) + "_t",
                _ => throw new NotSupportedException("A sequence element has no checked scalar carrier.")
            };
            return Scalar(name);
        }
        bool HasConst(StableId id)
        {
            NativeType type = source.GetType(id);
            return type.category == NativeTypeCategory.Qualified && type.qualifier?.Contains("Const", StringComparison.Ordinal) == true
                || type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified && type.elementType is StableId element && HasConst(element);
        }
    }
}
