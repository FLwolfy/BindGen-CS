using System;
using System.Linq;
using BGCS.Core.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Native;

namespace BGCS.Analysis.Bridge;

/// <summary>Independently checks generated standard-library access actions before source emission.</summary>
public sealed class StandardValueOperationVerifier
{
    /// <summary>Rejects family, arity, const, storage, conversion and ownership mismatches.</summary>
    /// <param name="module">The detached bridge graph.</param>
    /// <param name="export">The generated standard-library export.</param>
    public void Verify(
        NativeBridgePlan module,
        BridgeExport export
    ) {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(export);
        BridgeOperation operation = export.operation;
        if (operation.invocationExpression is not null || export.resultExpression is not null || export.parameters.Any(parameter => parameter.conversionExpression is not null))
            throw new InvalidOperationException("A built-in standard-library action cannot carry an unrelated native source escape hatch.");
        if (operation.kind != BridgeOperationKind.StandardValue || operation.declaration is not null || operation.castTarget is not null
            || operation.ownerType is not StableId ownerId || operation.standardValue is not StandardValueOperation standard
            || !Enum.IsDefined(standard.family) || !Enum.IsDefined(standard.action))
            Fail("The generated action has no exact standard-library owner and protocol.");
        StandardValueOperation facts = new StandardLibraryFactReader(module.familyBindings).Read(module.source, operation.ownerType!.Value)
            ?? throw new InvalidOperationException("The action owner is not a supported native standard-library specialization.");
        StandardValueOperation plan = operation.standardValue!;
        if (facts.family != plan.family || !facts.argumentTypes.SequenceEqual(plan.argumentTypes))
            Fail("The action changed its native standard-library family or template arguments.");
        NativeType owner = StandardLibraryFactReader.Resolve(module.source, operation.ownerType!.Value);
        StandardLibraryFactReader reader = new(module.familyBindings);
        bool cStr = reader.FindBinding(module.source, owner.id)?.stringAccess == CppStringAccess.CStrNullTerminated;
        bool construct = plan.action == StandardValueAction.Construct;
        bool mutates = plan.action is StandardValueAction.SetAt or StandardValueAction.Append or StandardValueAction.Insert or StandardValueAction.MoveUnique;
        bool readConst = !cStr || reader.ReadCStr(module.source, owner.id).isConst;
        BridgeParameter? self = export.parameters.SingleOrDefault(parameter => parameter.id == operation.instanceParameter);
        if (construct ? operation.instanceParameter is not null : self is null) Fail("The generated action has an incorrect instance requirement.");
        if (self is not null)
        {
            BridgeType storage = Type(self.type);
            if (self.nativeType != owner.id || self.conversion != BridgeConversionKind.PointerCast || !HandlePointer(storage, owner.id)
                || storage.isConst != (!mutates && readConst))
                Fail("The instance pointer has an incorrect owner or const qualification.");
        }
        BridgeParameter[] arguments = export.parameters.Where(parameter => parameter.id != operation.instanceParameter).ToArray();
        int arity = plan.action switch
        {
            StandardValueAction.GetAt or StandardValueAction.GetKeyAt or StandardValueAction.GetMappedAt or StandardValueAction.PathUtf8Byte or StandardValueAction.Append => 1,
            StandardValueAction.SetAt or StandardValueAction.Insert or StandardValueAction.CopyCodeUnits => 2,
            _ => 0
        };
        if (arguments.Length != arity) Fail("The generated action changed its protocol arity.");
        if (plan.action == StandardValueAction.CopyCodeUnits)
        {
            BridgeType destination = Type(arguments[0].type), capacity = Type(arguments[1].type);
            string element = facts.family == StandardValueKind.Path ? "uint8_t" : ScalarCarrier(StandardLibraryFactReader.Resolve(module.source, facts.argumentTypes[0]));
            if (arguments[0].name != "destination" || arguments[0].conversion != BridgeConversionKind.PointerCast
                || arguments[0].nativeType != (facts.family == StandardValueKind.Path ? owner.id : facts.argumentTypes[0])
                || destination.kind != BridgeTypeKind.Pointer || destination.isConst || destination.elementType is not StableId storage
                || Type(storage).kind != BridgeTypeKind.Scalar || Type(storage).cName != element
                || arguments[1].name != "capacity" || arguments[1].conversion != BridgeConversionKind.ScalarCast || arguments[1].nativeType != owner.id
                || capacity.kind != BridgeTypeKind.Scalar || capacity.cName != "uint64_t")
                Fail("A code-unit copy changed its exact writable destination or unsigned capacity carrier.");
        }
        bool indexed = plan.action is StandardValueAction.GetAt or StandardValueAction.SetAt or StandardValueAction.GetKeyAt or StandardValueAction.GetMappedAt or StandardValueAction.PathUtf8Byte;
        if (indexed && (arguments[0].name != "index" || arguments[0].nativeType != owner.id || arguments[0].conversion != BridgeConversionKind.ScalarCast
            || Type(arguments[0].type).kind != BridgeTypeKind.Scalar || Type(arguments[0].type).cName != "uint64_t"))
            Fail("An index must use the fixed-width checked protocol carrier.");
        bool allowed = plan.action switch
        {
            StandardValueAction.Construct => facts.family is not (StandardValueKind.Span or StandardValueKind.Path or StandardValueKind.Duration or StandardValueKind.TimePoint),
            StandardValueAction.Count => facts.family is StandardValueKind.String or StandardValueKind.Vector or StandardValueKind.Span or StandardValueKind.Array or StandardValueKind.Map or StandardValueKind.Set,
            StandardValueAction.GetAt => facts.family is StandardValueKind.String or StandardValueKind.Vector or StandardValueKind.Span or StandardValueKind.Array or StandardValueKind.Set,
            StandardValueAction.SetAt => facts.family is StandardValueKind.String or StandardValueKind.Vector or StandardValueKind.Span or StandardValueKind.Array,
            StandardValueAction.Append => facts.family is StandardValueKind.String or StandardValueKind.Vector or StandardValueKind.Set,
            StandardValueAction.HasValue => facts.family is StandardValueKind.Optional or StandardValueKind.Expected or StandardValueKind.UniquePointer or StandardValueKind.SharedPointer,
            StandardValueAction.GetValue => facts.family is StandardValueKind.Optional or StandardValueKind.Expected,
            StandardValueAction.GetError => facts.family == StandardValueKind.Expected,
            StandardValueAction.Discriminator or StandardValueAction.GetAlternative => facts.family == StandardValueKind.Variant,
            StandardValueAction.GetKeyAt or StandardValueAction.GetMappedAt or StandardValueAction.Insert => facts.family == StandardValueKind.Map,
            StandardValueAction.GetPointer => facts.family is StandardValueKind.UniquePointer or StandardValueKind.SharedPointer,
            StandardValueAction.CloneShared => facts.family == StandardValueKind.SharedPointer,
            StandardValueAction.MoveUnique => facts.family == StandardValueKind.UniquePointer,
            StandardValueAction.PathUtf8Count or StandardValueAction.PathUtf8Byte => facts.family == StandardValueKind.Path,
            StandardValueAction.DurationCount => facts.family == StandardValueKind.Duration,
            StandardValueAction.TimePointCount => facts.family == StandardValueKind.TimePoint,
            StandardValueAction.CopyCodeUnits => facts.family is StandardValueKind.String or StandardValueKind.Path,
            _ => false
        };
        if (!allowed || cStr && plan.action is StandardValueAction.SetAt or StandardValueAction.Append
            || cStr && construct && module.source.GetDeclaration(owner.declaration!.Value).isDefaultConstructible != true)
            Fail("The operation is not defined for this native standard-library family.");
        if (plan.action == StandardValueAction.GetAlternative)
        {
            if (plan.alternativeIndex is not int alternative || alternative < 0 || alternative >= facts.argumentTypes.Length)
                Fail("A variant access requires an in-range explicit native alternative.");
        }
        else if (plan.alternativeIndex is not null) Fail("An unrelated operation carries a variant alternative.");
        if (plan.action is StandardValueAction.SetAt or StandardValueAction.Append)
            CheckValue(arguments[^1], facts.argumentTypes[0]);
        if (plan.action == StandardValueAction.Insert)
        {
            CheckValue(arguments[0], facts.argumentTypes[0]);
            CheckValue(arguments[1], facts.argumentTypes[1]);
        }
        if (mutates && export.resultType is null)
        {
            if (export.nativeResultType is not null || export.resultRelease is not null || export.resultBorrowsInstance) Fail("A void mutation carries result ownership.");
            return;
        }
        if (export.resultType is not StableId resultId) Fail("The access or construction action discarded its result.");
        BridgeType result = Type(export.resultType!.Value);
        string? fixedScalar = plan.action switch
        {
            StandardValueAction.Count or StandardValueAction.Discriminator or StandardValueAction.PathUtf8Count or StandardValueAction.CopyCodeUnits => "uint64_t",
            StandardValueAction.HasValue or StandardValueAction.PathUtf8Byte => "uint8_t",
            _ => null
        };
        if (fixedScalar is not null)
        {
            if (result.kind != BridgeTypeKind.Scalar || result.cName != fixedScalar || export.nativeResultType is not null
                || export.resultConversion != BridgeConversionKind.ScalarCast || export.resultRelease is not null || export.resultBorrowsInstance)
                Fail("The generated scalar protocol changed its fixed C representation.");
            return;
        }
        StableId expectedNative = plan.action switch
        {
            StandardValueAction.Construct or StandardValueAction.CloneShared or StandardValueAction.MoveUnique => owner.id,
            StandardValueAction.GetError or StandardValueAction.GetMappedAt => facts.argumentTypes[1],
            StandardValueAction.GetAlternative => facts.argumentTypes[plan.alternativeIndex!.Value],
            StandardValueAction.TimePointCount => ReadDurationRepresentation(facts.argumentTypes[1]),
            _ => facts.argumentTypes[0]
        };
        if (export.nativeResultType != expectedNative) Fail("The result changed its native element or representation identity.");
        NativeType nativeResult = StandardLibraryFactReader.Resolve(module.source, expectedNative);
        if (plan.action == StandardValueAction.GetPointer)
        {
            if (result.kind != BridgeTypeKind.Pointer || export.resultConversion != BridgeConversionKind.PointerCast
                || !export.resultBorrowsInstance || export.resultRelease is not null)
                Fail("A smart-pointer access must borrow a pointer from its holder.");
            if (nativeResult.category == NativeTypeCategory.Record && !HandlePointer(result, nativeResult.id)) Fail("The borrowed pointee belongs to another native record.");
            return;
        }
        if (nativeResult.category == NativeTypeCategory.Record)
        {
            if (result.kind == BridgeTypeKind.Record && result.nativeType == nativeResult.id && export.resultConversion == BridgeConversionKind.RecordCopy
                && export.resultRelease is null && !export.resultBorrowsInstance) return;
            BridgeExport? release = module.exports.SingleOrDefault(item => item.id == export.resultRelease);
            if (!HandlePointer(result, nativeResult.id) || result.isConst || export.resultConversion != BridgeConversionKind.OwnedValue || export.resultBorrowsInstance
                || release is null || release.operation.kind != BridgeOperationKind.Destroy || release.operation.ownerType != nativeResult.id
                || release.parameters.Length != 1 || release.parameters[0].type != result.id || release.resultType is not null)
                Fail("An owned standard-library value copy requires its exact native destruction pair.");
        }
        else if (nativeResult.category == NativeTypeCategory.Pointer)
        {
            if (result.kind != BridgeTypeKind.Pointer || export.resultConversion != BridgeConversionKind.PointerCast || export.resultRelease is not null)
                Fail("A copied native pointer has no matching pointer carrier.");
        }
        else if (result.kind is not (BridgeTypeKind.Scalar or BridgeTypeKind.Enumeration) || export.resultConversion != BridgeConversionKind.ScalarCast
            || export.resultRelease is not null || export.resultBorrowsInstance)
            Fail("A copied scalar requires a scalar representation without owned cleanup.");

        BridgeType Type(StableId id) => module.types.Single(item => item.id == id);
        bool HandlePointer(
            BridgeType pointer,
            StableId native
        ) => pointer.kind == BridgeTypeKind.Pointer && pointer.elementType is StableId element
            && Type(element) is { kind: BridgeTypeKind.Handle, nativeType: StableId identity } && identity == native;
        StableId ReadDurationRepresentation(StableId durationId)
        {
            StandardValueOperation duration = new StandardLibraryFactReader(module.familyBindings).Read(module.source, durationId)
                ?? throw new InvalidOperationException("The time_point has no detached duration argument.");
            if (duration.family != StandardValueKind.Duration) Fail("The time_point argument is not a duration.");
            return duration.argumentTypes[0];
        }
        void CheckValue(
            BridgeParameter parameter,
            StableId nativeId
        ) {
            NativeType native = StandardLibraryFactReader.Resolve(module.source, nativeId);
            BridgeType storage = Type(parameter.type);
            BridgeConversionKind expected = native.category == NativeTypeCategory.Record ? storage.kind == BridgeTypeKind.Pointer
                && storage.isConst && storage.elementType is StableId dto && Type(dto).kind == BridgeTypeKind.Record && Type(dto).nativeType == native.id
                    ? BridgeConversionKind.RecordCopy : BridgeConversionKind.Dereference
                : native.category == NativeTypeCategory.Pointer ? BridgeConversionKind.PointerCast : BridgeConversionKind.ScalarCast;
            if (parameter.nativeType != nativeId || parameter.conversion != expected || storage.kind == BridgeTypeKind.Handle)
                Fail("A mutation value changed its corresponding native element identity or conversion.");
        }
        void Fail(string reason) => throw new InvalidOperationException($"Bridge export '{export.name}': {reason}");
    }

    private static string ScalarCarrier(NativeType unit) => unit.category == NativeTypeCategory.Primitive && unit.size is 1 or 2 or 4 or 8 && unit.isSigned is bool signed
        ? (signed ? "int" : "uint") + (unit.size.Value * 8).ToString(System.Globalization.CultureInfo.InvariantCulture) + "_t"
        : throw new InvalidOperationException("A string copy requires an exact integral C scalar code-unit carrier.");
}
