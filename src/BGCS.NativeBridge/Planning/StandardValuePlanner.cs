using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using BGCS.Analysis.Bridge;
using BGCS.Core.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Native;

namespace BGCS.NativeBridge.Planning;

/// <summary>Plans finite checked access protocols for opaque standard-library values.</summary>
internal sealed class StandardValuePlanner
{
    private readonly NativeModule m_source;
    private readonly Func<StableId, BridgeType> m_lower;
    private readonly Func<StableId, BridgeType> m_storage;
    private readonly Func<StableId, BridgeType> m_handle;
    private readonly Func<StableId, BridgeExport> m_release;
    private readonly Func<BridgeType, bool, BridgeType> m_pointer;
    private readonly Action<BridgeType> m_register;
    private readonly Func<string, StableId, string> m_name;
    private readonly StandardLibraryFactReader m_families;

    internal StandardValuePlanner(
        NativeModule source,
        Func<StableId, BridgeType> lower,
        Func<StableId, BridgeExport> release,
        Func<BridgeType, bool, BridgeType> pointer,
        Action<BridgeType> register,
        Func<string, StableId, string> name,
        Func<StableId, BridgeType> storage,
        Func<StableId, BridgeType> handle,
        StandardLibraryFactReader families
    ) { m_source = source; m_lower = lower; m_release = release; m_pointer = pointer; m_register = register; m_name = name; m_storage = storage; m_handle = handle; m_families = families; }

    internal ImmutableArray<BridgeExport> Plan(BridgeType handle)
    {
        StableId owner = handle.nativeType!.Value;
        StandardValueOperation? facts = m_families.Read(m_source, owner);
        if (facts is null) return [];
        NativeDeclaration declaration = m_source.GetDeclaration(m_source.GetType(owner).declaration!.Value);
        List<BridgeExport> exports = [];
        BridgeType count = Scalar("uint64_t");
        BridgeType flag = Scalar("uint8_t");
        StandardValueKind family = facts.family;
        bool cStr = m_families.FindBinding(m_source, owner)?.stringAccess == CppStringAccess.CStrNullTerminated;
        bool readConst = !cStr || m_families.ReadCStr(m_source, owner).isConst;
        if (family is not (StandardValueKind.Span or StandardValueKind.Path or StandardValueKind.Duration or StandardValueKind.TimePoint)
            && (!cStr || declaration.isDefaultConstructible == true))
            Add(StandardValueAction.Construct, [], m_pointer(handle, false), owner, BridgeConversionKind.OwnedValue, false, m_release(owner).id);
        if (family is StandardValueKind.String or StandardValueKind.Vector or StandardValueKind.Span or StandardValueKind.Array or StandardValueKind.Map or StandardValueKind.Set)
            Add(StandardValueAction.Count, [], count, isConst: readConst);
        NativeType? stringUnit = family == StandardValueKind.String ? StandardLibraryFactReader.Resolve(m_source, facts.argumentTypes[0]) : null;
        if (family == StandardValueKind.Path || stringUnit is { category: NativeTypeCategory.Primitive, size: 1 or 2 or 4 or 8, isSigned: not null }
            && stringUnit.primitiveKind is not ("Void" or "Bool" or "Float" or "Double" or "LongDouble"))
        {
            StableId unit = family == StandardValueKind.String ? facts.argumentTypes[0] : owner;
            BridgeType element = family == StandardValueKind.String ? m_lower(unit) : flag;
            Add(StandardValueAction.CopyCodeUnits,
                [new(new("bridge-standard-copy-destination:" + owner.value), "destination", m_pointer(element, false).id, unit, BridgeConversionKind.PointerCast),
                 new(new("bridge-standard-copy-capacity:" + owner.value), "capacity", count.id, owner, BridgeConversionKind.ScalarCast)], count, isConst: readConst);
        }
        if (family is StandardValueKind.String or StandardValueKind.Vector or StandardValueKind.Span or StandardValueKind.Array or StandardValueKind.Set)
        {
            Result(StandardValueAction.GetAt, [Index()], facts.argumentTypes[0], isConst: readConst);
            if (!cStr && family != StandardValueKind.Set && !IsConst(facts.argumentTypes[0]))
                Add(StandardValueAction.SetAt, [Index(), Value("value", facts.argumentTypes[0])], isConst: false);
            if (!cStr && family is StandardValueKind.String or StandardValueKind.Vector or StandardValueKind.Set)
                Add(StandardValueAction.Append, [Value("value", facts.argumentTypes[0])], isConst: false);
        }
        if (family == StandardValueKind.Map)
        {
            Result(StandardValueAction.GetKeyAt, [Index()], facts.argumentTypes[0]);
            Result(StandardValueAction.GetMappedAt, [Index()], facts.argumentTypes[1]);
            Add(StandardValueAction.Insert, [Value("key", facts.argumentTypes[0]), Value("value", facts.argumentTypes[1])], isConst: false);
        }
        if (family is StandardValueKind.Optional or StandardValueKind.Expected or StandardValueKind.UniquePointer or StandardValueKind.SharedPointer)
            Add(StandardValueAction.HasValue, [], flag);
        if (family is StandardValueKind.Optional or StandardValueKind.Expected)
        {
            if (StandardLibraryFactReader.Resolve(m_source, facts.argumentTypes[0]).primitiveKind != "Void")
                Result(StandardValueAction.GetValue, [], facts.argumentTypes[0]);
            if (family == StandardValueKind.Expected) Result(StandardValueAction.GetError, [], facts.argumentTypes[1]);
        }
        if (family == StandardValueKind.Variant)
        {
            Add(StandardValueAction.Discriminator, [], count);
            for (int index = 0; index < facts.argumentTypes.Length; index++) Result(StandardValueAction.GetAlternative, [], facts.argumentTypes[index], index);
        }
        if (family == StandardValueKind.Path)
        {
            Add(StandardValueAction.PathUtf8Count, [], count);
            Add(StandardValueAction.PathUtf8Byte, [Index()], flag);
        }
        if (family is StandardValueKind.Duration or StandardValueKind.TimePoint)
        {
            StableId representation = facts.argumentTypes[0];
            if (family == StandardValueKind.TimePoint)
            {
                StandardValueOperation duration = m_families.Read(m_source, facts.argumentTypes[1])
                    ?? throw new NotSupportedException("A time_point requires detached duration representation and period facts.");
                if (duration.family != StandardValueKind.Duration) throw new NotSupportedException("A time_point duration argument is not a native duration.");
                representation = duration.argumentTypes[0];
            }
            Result(family == StandardValueKind.Duration ? StandardValueAction.DurationCount : StandardValueAction.TimePointCount, [], representation);
        }
        if (family is StandardValueKind.UniquePointer or StandardValueKind.SharedPointer)
        {
            NativeType element = StandardLibraryFactReader.Resolve(m_source, facts.argumentTypes[0]);
            if (element.category == NativeTypeCategory.Array)
                throw new NotSupportedException("Smart-pointer array specializations require an explicit extent and buffer contract.");
            BridgeType pointee = m_lower(element.id);
            BridgeType pointer = element.category == NativeTypeCategory.Record ? m_pointer(m_handle(element.id), IsConst(facts.argumentTypes[0])) : m_pointer(pointee, IsConst(facts.argumentTypes[0]));
            Add(StandardValueAction.GetPointer, [], pointer, element.id, BridgeConversionKind.PointerCast, borrows: true);
            Add(family == StandardValueKind.SharedPointer ? StandardValueAction.CloneShared : StandardValueAction.MoveUnique, [],
                m_pointer(handle, false), owner, BridgeConversionKind.OwnedValue, family == StandardValueKind.SharedPointer, m_release(owner).id);
        }
        return exports.ToImmutableArray();

        BridgeType Scalar(string spelling)
        {
            BridgeType scalar = new(new("bridge-scalar:" + spelling), BridgeTypeKind.Scalar, spelling);
            m_register(scalar);
            return scalar;
        }

        BridgeParameter Index() => new(new("bridge-standard-index:" + owner.value), "index", count.id, owner, BridgeConversionKind.ScalarCast);

        BridgeParameter Value(
            string name,
            StableId nativeId
        ) {
            NativeType native = StandardLibraryFactReader.Resolve(m_source, nativeId);
            if (native.category is not (NativeTypeCategory.Primitive or NativeTypeCategory.Record or NativeTypeCategory.Enumeration or NativeTypeCategory.Pointer))
                throw new NotSupportedException($"Standard-library element '{native.spelling}' requires an explicit concrete value lowering.");
            return new(new("bridge-standard-value:" + owner.value + ":" + name), name, m_lower(nativeId).id, nativeId,
                native.category == NativeTypeCategory.Record ? m_storage(nativeId).kind == BridgeTypeKind.Record ? BridgeConversionKind.RecordCopy : BridgeConversionKind.Dereference
                : native.category == NativeTypeCategory.Pointer ? BridgeConversionKind.PointerCast : BridgeConversionKind.ScalarCast);
        }

        void Result(
            StandardValueAction action,
            ImmutableArray<BridgeParameter> parameters,
            StableId nativeId,
            int? alternative = null,
            bool isConst = true
        ) {
            NativeType native = StandardLibraryFactReader.Resolve(m_source, nativeId);
            if (native.category is not (NativeTypeCategory.Primitive or NativeTypeCategory.Record or NativeTypeCategory.Enumeration or NativeTypeCategory.Pointer))
                throw new NotSupportedException($"Standard-library result '{native.spelling}' requires an explicit concrete value lowering.");
            BridgeType? record = native.category == NativeTypeCategory.Record && m_storage(nativeId).kind == BridgeTypeKind.Record ? m_storage(nativeId) : null;
            Add(action, parameters, record ?? m_lower(nativeId), nativeId, record is not null ? BridgeConversionKind.RecordCopy : native.category == NativeTypeCategory.Record ? BridgeConversionKind.OwnedValue
                : native.category == NativeTypeCategory.Pointer ? BridgeConversionKind.PointerCast : BridgeConversionKind.ScalarCast,
                release: native.category == NativeTypeCategory.Record && record is null ? m_release(native.id).id : null, alternative: alternative, isConst: isConst);
        }

        void Add(
            StandardValueAction action,
            ImmutableArray<BridgeParameter> arguments,
            BridgeType? result = null,
            StableId? nativeResult = null,
            BridgeConversionKind conversion = BridgeConversionKind.ScalarCast,
            bool isConst = true,
            StableId? release = null,
            bool borrows = false,
            int? alternative = null
        ) {
            StableId exportId = new("bridge-standard:" + owner.value + ":" + action + (alternative is int index ? ":" + index.ToString(CultureInfo.InvariantCulture) : string.Empty));
            bool needsInstance = action != StandardValueAction.Construct;
            StableId self = new(exportId.value + ":self");
            ImmutableArray<BridgeParameter> parameters = needsInstance
                ? [new(self, "bgcs_self", m_pointer(handle, isConst).id, owner, BridgeConversionKind.PointerCast), .. arguments] : arguments;
            exports.Add(new(exportId, m_name(declaration.qualifiedName + "_" + action + (alternative is int alt ? "_" + alt.ToString(CultureInfo.InvariantCulture) : string.Empty), exportId), parameters,
                new(BridgeOperationKind.StandardValue, null, owner, needsInstance ? self : null)
                { standardValue = facts with { action = action, alternativeIndex = alternative } })
            { resultType = result?.id, nativeResultType = nativeResult, resultConversion = conversion, resultRelease = release, resultBorrowsInstance = borrows });
        }

        bool IsConst(StableId id)
        {
            NativeType type = m_source.GetType(id);
            return type.category == NativeTypeCategory.Qualified && type.qualifier?.Contains("Const", StringComparison.Ordinal) == true
                || type.category is NativeTypeCategory.Qualified or NativeTypeCategory.Alias && type.elementType is StableId element && IsConst(element);
        }
    }
}
