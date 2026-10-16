using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Native;

namespace BGCS.Analysis.Bridge;

/// <summary>Associates finite source value operations with their independently reparsed final C carriers.</summary>
public sealed class NativeValueProtocolBuilder
{
    /// <summary>Attaches checked value semantics without changing native storage, imports or ownership.</summary>
    /// <param name="bridge">The verified typed source operation plan.</param>
    /// <param name="module">Its merged final C ABI.</param>
    /// <returns>The same native ABI with immutable value protocols.</returns>
    public InteropModule Attach(
        CppBridgeModule bridge,
        InteropModule module
    ) {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(module);
        if (bridge.source.targetId != module.targetId || bridge.source.pointerSize != module.pointerSize || bridge.source.byteOrder != module.byteOrder)
            throw new InvalidOperationException("A value protocol cannot cross native target identities.");
        Dictionary<StableId, InteropFunction> functions = module.functions.ToDictionary(function => function.id);
        Dictionary<StableId, BridgeExport[]> operations = bridge.exports.Where(export => export.operation.standardValue is not null)
            .GroupBy(export => StandardLibraryFactReader.Resolve(bridge.source, export.operation.ownerType!.Value).id)
            .ToDictionary(group => group.Key, group => group.ToArray());
        Dictionary<StableId, BridgeExport[]> cleanup = bridge.exports.Where(export => export.operation.kind == BridgeOperationKind.Destroy)
            .GroupBy(export => StandardLibraryFactReader.Resolve(bridge.source, export.operation.ownerType!.Value).id)
            .ToDictionary(group => group.Key, group => group.ToArray());
        StandardLibraryFactReader reader = new(bridge.familyBindings);
        ImmutableArray<InteropType> types = module.types.Select(type =>
        {
            if (type.category != NativeTypeCategory.Record || type.size is not null || type.sourceNativeType is not StableId sourceId) return type;
            NativeType source = StandardLibraryFactReader.Resolve(bridge.source, sourceId);
            if (!operations.TryGetValue(source.id, out BridgeExport[]? exports)) return type;
            StandardValueOperation facts = reader.Read(bridge.source, source.id) ?? throw new InvalidOperationException("A generated value protocol lost its detached family facts.");
            CppFamilyBinding? binding = reader.FindBinding(bridge.source, source.id);
            Evidence evidence = binding?.evidence ?? new(EvidenceLevel.Declared, "typed-native-value:" + source.id.value);
            ImmutableArray<NativeValueExport> access = exports.Select(export => new NativeValueExport(Action(export.operation.standardValue!.action), export.id,
                export.operation.instanceParameter, export.parameters.Where(parameter => parameter.id != export.operation.instanceParameter).Select(parameter => parameter.id).ToImmutableArray())
            { alternativeIndex = export.operation.standardValue.alternativeIndex }).ToImmutableArray();
            ImmutableArray<StableId> sourceElements = facts.argumentTypes;
            if (facts.family == StandardValueKind.TimePoint)
            {
                StandardValueOperation duration = reader.Read(bridge.source, facts.argumentTypes[1]) ?? throw new InvalidOperationException("A time-point protocol lost its duration facts.");
                sourceElements = [duration.argumentTypes[0]];
            }
            ImmutableArray<NativeValueElement>.Builder elements = ImmutableArray.CreateBuilder<NativeValueElement>();
            for (int index = 0; index < sourceElements.Length; index++)
            {
                NativeType element = StandardLibraryFactReader.Resolve(bridge.source, sourceElements[index]);
                NativeValueOperationKind action = facts.family switch
                {
                    StandardValueKind.Map => index == 0 ? NativeValueOperationKind.GetKeyAt : NativeValueOperationKind.GetMappedAt,
                    StandardValueKind.Expected => index == 0 ? NativeValueOperationKind.GetValue : NativeValueOperationKind.GetError,
                    StandardValueKind.Optional => NativeValueOperationKind.GetValue,
                    StandardValueKind.Variant => NativeValueOperationKind.GetAlternative,
                    StandardValueKind.Duration => NativeValueOperationKind.DurationCount,
                    StandardValueKind.TimePoint => NativeValueOperationKind.TimePointCount,
                    StandardValueKind.UniquePointer or StandardValueKind.SharedPointer => NativeValueOperationKind.GetPointer,
                    _ => NativeValueOperationKind.GetAt
                };
                NativeValueExport? getter = access.SingleOrDefault(operation => operation.kind == action
                    && (action != NativeValueOperationKind.GetAlternative || operation.alternativeIndex == index));
                StableId? carrier = getter is null ? null : ResultCarrier(getter.function);
                if (carrier is null && element.primitiveKind != "Void") throw new InvalidOperationException("A native value element lost its checked C copy carrier.");
                elements.Add(new(sourceElements[index], carrier, element.primitiveKind == "Bool") { isVoid = element.primitiveKind == "Void" });
            }
            NativeValueProtocol protocol = new(new("native-value:" + source.id.value), Family(facts.family), type.id, source.id, access, elements.ToImmutable(), evidence)
            {
                releaseExport = cleanup.TryGetValue(source.id, out BridgeExport[]? releases)
                    ? releases.Length == 1 ? releases[0].id : throw new InvalidOperationException("A native value holder has ambiguous cleanup exports.") : null
            };
            if (protocol.releaseExport is not null)
                protocol = protocol with { scopedRelease = StandardValueReleaseFacts.Read(bridge.source, source.id) };
            if (facts.family == StandardValueKind.String)
            {
                NativeType unit = StandardLibraryFactReader.Resolve(bridge.source, sourceElements[0]);
                if (unit.size is not (1 or 2 or 4)) throw new NotSupportedException("A native string protocol needs a supported compiler-established code-unit width.");
                protocol = protocol with
                {
                    text = new(checked((int)unit.size.Value), unit.isSigned, bridge.source.byteOrder,
                    new(EvidenceLevel.Declared, "native-code-unit-storage:" + unit.id.value))
                    {
                        encoding = binding?.stringEncoding ?? NativeTextEncoding.Unknown,
                        encodingEvidence = binding?.evidence ?? new(EvidenceLevel.Unknown, "no-native-string-encoding"),
                        zeroTerminated = binding?.stringAccess == CppStringAccess.CStrNullTerminated,
                        permitsEmbeddedZero = binding?.stringAccess != CppStringAccess.CStrNullTerminated,
                        maximumCodeUnits = binding?.cStr?.maximumCodeUnits,
                        terminationEvidence = binding?.cStr?.nullTerminated,
                        readOnlyReceiver = binding?.stringAccess != CppStringAccess.CStrNullTerminated || reader.ReadCStr(bridge.source, source.id).isConst
                    }
                };
            }
            if (facts.family is StandardValueKind.String or StandardValueKind.Vector or StandardValueKind.Span or StandardValueKind.Array or StandardValueKind.Map or StandardValueKind.Set)
            {
                NativeDeclaration owner = bridge.source.GetDeclaration(source.declaration!.Value);
                ulong? extent = facts.family == StandardValueKind.Array && owner.templateArguments.Length > 1
                    && owner.templateArguments[1] is { kind: "AsInteger" or "Integral", type: null } extentFact
                    && ulong.TryParse(extentFact.spelling, NumberStyles.None, CultureInfo.InvariantCulture, out ulong fixedLength) ? fixedLength : null;
                protocol = protocol with
                {
                    sequence = new(evidence)
                    {
                        fixedLength = extent,
                        isBorrowedView = facts.family == StandardValueKind.Span,
                        readLifetime = binding?.cStr?.readableLifetime ?? new(NativeLifetimeKind.Unknown, null, new(EvidenceLevel.Unknown, "no-native-sequence-owner")),
                        isPackedBoolean = facts.family == StandardValueKind.Vector && elements[0].isBoolean
                        && new StandardLibraryFactReader().Read(bridge.source, source.id)?.family == StandardValueKind.Vector
                    }
                };
            }
            if (facts.family == StandardValueKind.Path) protocol = protocol with { path = new(binding?.pathFormat ?? NativePathFormat.GenericUtf8, evidence) };
            if (facts.family is StandardValueKind.Duration or StandardValueKind.TimePoint) protocol = protocol with { period = reader.ReadPeriod(bridge.source, source.id) };
            if (facts.family == StandardValueKind.TimePoint) protocol = protocol with { clock = new(facts.argumentTypes[0], evidence) };
            return type with { valueProtocol = protocol };
        }).ToImmutableArray();
        InteropModule result = new(module.libraryId, module.targetId, module.pointerSize, types, module.functions, module.diagnostics)
        { byteOrder = module.byteOrder, constants = module.constants, virtualProxies = module.virtualProxies, functionAliases = module.functionAliases, stringConstants = module.stringConstants };
        new NativeValueProtocolVerifier().Verify(result);
        return result;

        StableId ResultCarrier(StableId export)
        {
            InteropFunction function = functions[export];
            InteropParameter output = function.parameters.Single(parameter => parameter.id == function.error?.valueParameter);
            InteropType pointer = Resolve(output.type);
            return pointer.category == NativeTypeCategory.Pointer && pointer.elementType is StableId element ? element
                : throw new InvalidOperationException("A native value accessor lost its actual C result-out carrier.");
        }
        InteropType Resolve(StableId id)
        {
            HashSet<StableId> seen = [];
            InteropType type = module.GetType(id);
            while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
            {
                if (!seen.Add(type.id) || type.elementType is not StableId underlying) throw new InvalidOperationException("A native value C carrier has an incomplete alias chain.");
                type = module.GetType(underlying);
            }
            return type;
        }
    }

    private static NativeValueFamily Family(StandardValueKind kind) => kind switch
    {
        StandardValueKind.String => NativeValueFamily.String,
        StandardValueKind.Vector => NativeValueFamily.Vector,
        StandardValueKind.Span => NativeValueFamily.Span,
        StandardValueKind.Array => NativeValueFamily.Array,
        StandardValueKind.Map => NativeValueFamily.Map,
        StandardValueKind.Set => NativeValueFamily.Set,
        StandardValueKind.Optional => NativeValueFamily.Optional,
        StandardValueKind.Variant => NativeValueFamily.Variant,
        StandardValueKind.Expected => NativeValueFamily.Expected,
        StandardValueKind.Path => NativeValueFamily.Path,
        StandardValueKind.Duration => NativeValueFamily.Duration,
        StandardValueKind.TimePoint => NativeValueFamily.TimePoint,
        StandardValueKind.UniquePointer => NativeValueFamily.UniqueOwner,
        StandardValueKind.SharedPointer => NativeValueFamily.SharedOwner,
        _ => throw new InvalidOperationException("A native value family is undefined.")
    };

    private static NativeValueOperationKind Action(StandardValueAction kind) => kind switch
    {
        StandardValueAction.Construct => NativeValueOperationKind.Construct,
        StandardValueAction.Count => NativeValueOperationKind.Count,
        StandardValueAction.GetAt => NativeValueOperationKind.GetAt,
        StandardValueAction.SetAt => NativeValueOperationKind.SetAt,
        StandardValueAction.Append => NativeValueOperationKind.Append,
        StandardValueAction.HasValue => NativeValueOperationKind.HasValue,
        StandardValueAction.GetValue => NativeValueOperationKind.GetValue,
        StandardValueAction.GetError => NativeValueOperationKind.GetError,
        StandardValueAction.Discriminator => NativeValueOperationKind.Discriminator,
        StandardValueAction.GetAlternative => NativeValueOperationKind.GetAlternative,
        StandardValueAction.GetKeyAt => NativeValueOperationKind.GetKeyAt,
        StandardValueAction.GetMappedAt => NativeValueOperationKind.GetMappedAt,
        StandardValueAction.Insert => NativeValueOperationKind.Insert,
        StandardValueAction.GetPointer => NativeValueOperationKind.GetPointer,
        StandardValueAction.CloneShared => NativeValueOperationKind.CloneOwner,
        StandardValueAction.MoveUnique => NativeValueOperationKind.MoveOwner,
        StandardValueAction.PathUtf8Count => NativeValueOperationKind.PathUtf8Count,
        StandardValueAction.PathUtf8Byte => NativeValueOperationKind.PathUtf8Byte,
        StandardValueAction.DurationCount => NativeValueOperationKind.DurationCount,
        StandardValueAction.CopyCodeUnits => NativeValueOperationKind.CopyCodeUnits,
        StandardValueAction.TimePointCount => NativeValueOperationKind.TimePointCount,
        _ => throw new InvalidOperationException("A native value operation is undefined.")
    };
}
