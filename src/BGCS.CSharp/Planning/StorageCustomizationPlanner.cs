using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Managed;
using BGCS.Contracts.Native;

namespace BGCS.CSharp.Planning;

internal static class StorageCustomizationPlanner
{
    internal static CSharpEmissionPlan Apply(CSharpEmissionPlan plan) => plan with
    {
        storage = plan.storage.Select(storage =>
        {
            ManagedRecordPresentation? presentation = plan.api.types.Single(type => type.nativeType == storage.nativeType).recordPresentation;
            if (presentation is null) return storage;
            InteropType native = plan.api.interop.GetType(storage.nativeType);
            if (native.category != NativeTypeCategory.Record || storage.arrayLength is not null || storage.enumUnderlyingType is not null
                || storage.size is null && (presentation.emitSizeConstant || !presentation.constructorFields.IsDefaultOrEmpty || presentation.validity is not null))
                throw new NotSupportedException("Value record conveniences require a complete independently planned native storage carrier.");
            if (presentation.constructorFields.IsDefault || presentation.fieldDocumentation.IsDefault)
                throw new ArgumentException("Bound record presentation collections must be initialized.");
            HashSet<string> names = storage.fields.Select(field => field.name.TrimStart('@'))
                .Concat(storage.bitfields.Select(field => field.name.TrimStart('@'))).Concat(storage.flexibleArrays.Select(field => field.name.TrimStart('@')))
                .Append(storage.name.TrimStart('@')).ToHashSet(StringComparer.Ordinal);
            if (presentation.emitSizeConstant && !names.Add("NativeSize")) throw new ArgumentException("The native size constant conflicts with a storage member.");
            ImmutableArray<CSharpFieldPlan> fields = storage.fields.Select(field => field with
            { documentation = presentation.fieldDocumentation.SingleOrDefault(item => item.nativeField == field.nativeField)?.documentation ?? field.documentation }).ToImmutableArray();
            foreach (ManagedFieldDocumentation field in presentation.fieldDocumentation)
                if (!fields.Any(candidate => candidate.nativeField == field.nativeField))
                    throw new NotSupportedException("Documentation overrides currently require an ordinary addressable native field.");
            CSharpFieldPlan Field(StableId identity) => fields.SingleOrDefault(field => field.nativeField == identity)
                ?? throw new NotSupportedException("A data constructor or validity property requires an ordinary addressable field; bitfield and flexible-array protocols remain separate.");
            ImmutableArray<CSharpFieldPlan> constructor = presentation.constructorFields.Select(Field).ToImmutableArray();
            if (constructor.Select(field => field.nativeField).Distinct().Count() != constructor.Length)
                throw new ArgumentException("A data constructor cannot assign one native field more than once.");
            for (int left = 0; left < constructor.Length; left++)
                for (int right = left + 1; right < constructor.Length; right++)
                {
                    InteropField a = native.fields.Single(field => field.id == constructor[left].nativeField);
                    InteropField b = native.fields.Single(field => field.id == constructor[right].nativeField);
                    long aSize = plan.api.interop.GetType(a.type).size ?? throw new NotSupportedException("A constructor field has unknown native extent.");
                    long bSize = plan.api.interop.GetType(b.type).size ?? throw new NotSupportedException("A constructor field has unknown native extent.");
                    if (constructor[left].offset < constructor[right].offset + bSize && constructor[right].offset < constructor[left].offset + aSize)
                        throw new NotSupportedException("A data constructor cannot independently assign overlapping native storage fields.");
                }
            ImmutableArray<CSharpRecordArrayCopyPlan>.Builder arrayCopies = ImmutableArray.CreateBuilder<CSharpRecordArrayCopyPlan>();
            ImmutableArray<CSharpRecordArrayCopyOmission>.Builder arrayOmissions = ImmutableArray.CreateBuilder<CSharpRecordArrayCopyOmission>();
            foreach (CSharpFieldPlan field in constructor)
            {
                InteropField sourceField = native.fields.Single(candidate => candidate.id == field.nativeField);
                InteropType array = Resolve(sourceField.type);
                if (array.category != NativeTypeCategory.Array) continue;
                string? restriction = ArrayRestriction(array, out CSharpStoragePlan? arrayStorage);
                if (restriction is not null) arrayOmissions.Add(new(sourceField.id, field.name, restriction));
                else arrayCopies.Add(new(sourceField.id, field.name, arrayStorage!.arrayElementType!, arrayStorage.arrayLength!.Value));
            }
            CSharpRecordValidityPlan? validity = null;
            if (presentation.validity is { } rule)
            {
                CSharpFieldPlan field = Field(rule.nativeField);
                InteropType value = plan.api.interop.GetType(native.fields.Single(candidate => candidate.id == rule.nativeField).type);
                HashSet<StableId> seen = [];
                while (value.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified && value.elementType is StableId element && seen.Add(value.id))
                    value = plan.api.interop.GetType(element);
                if (value.category is not (NativeTypeCategory.Primitive or NativeTypeCategory.Enumeration or NativeTypeCategory.Pointer))
                    throw new NotSupportedException("A sentinel validity comparison requires scalar, enumeration or pointer storage.");
                string property = CSharpNames.Identifier(rule.propertyName);
                if (!names.Add(property.TrimStart('@'))) throw new ArgumentException("The validity property conflicts with a storage member.");
                string? constant = rule.invalidValue.kind == ManagedDefaultValueKind.ConstantReference ? "__BgcsInvalid_" + property.TrimStart('@') : null;
                if (constant is not null && (!names.Add(constant) || value.category == NativeTypeCategory.Pointer))
                    throw new ArgumentException("The referenced sentinel requires a distinct scalar compile-time constant.");
                validity = new(property, field.name, field.type, ManagedDefaultValuePlanner.Expression(rule.invalidValue, field.type, plan), constant);
            }
            return storage with
            {
                documentation = presentation.documentation ?? storage.documentation,
                fields = fields,
                presentation = new(presentation.emitSizeConstant, constructor, validity)
                { arrayCopies = arrayCopies.ToImmutable(), arrayCopyOmissions = arrayOmissions.ToImmutable() }
            };

            InteropType Resolve(StableId id)
            {
                HashSet<StableId> visited = [];
                InteropType type = plan.api.interop.GetType(id);
                while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
                {
                    if (!visited.Add(type.id) || type.elementType is not StableId next)
                        throw new InvalidOperationException("A data constructor field has an incomplete or cyclic native type graph.");
                    type = plan.api.interop.GetType(next);
                }
                return type;
            }

            string? ArrayRestriction(
                InteropType array,
                out CSharpStoragePlan? carrier
            ) {
                carrier = plan.storage.SingleOrDefault(candidate => candidate.nativeType == array.id);
                if (array.arrayLength is not > 0 || array.arrayLength > int.MaxValue || array.elementType is not StableId element
                    || carrier is null || carrier.arrayLength != array.arrayLength || carrier.arrayElementType is null || carrier.size != array.size)
                    return "The fixed array has no exact complete generated inline storage; use its raw carrier constructor.";
                if (carrier.pointerArrayWidth is not null)
                    return "Pointer arrays require their checked raw slot carrier rather than a managed generic Span of pointers.";
                InteropType scalar = Resolve(element);
                bool integer = scalar.primitiveKind is "Char" or "SignedChar" or "UnsignedChar" or "Short" or "UnsignedShort" or "Int" or "UnsignedInt" or "Long" or "UnsignedLong" or "LongLong" or "UnsignedLongLong";
                bool floating = scalar.primitiveKind == "Float" && scalar.size == 4 || scalar.primitiveKind == "Double" && scalar.size == 8;
                if (scalar.category != NativeTypeCategory.Primitive || scalar.size is not (1 or 2 or 4 or 8)
                    || !(integer && scalar.isSigned is not null || floating)
                    || carrier.arrayElementType is not ("byte" or "sbyte" or "short" or "ushort" or "int" or "uint" or "long" or "ulong" or "float" or "double"))
                    return "Only exact fixed primitive numeric arrays have a Span copy constructor; Boolean, enum, record and special scalar arrays retain their raw carrier constructor.";
                if (array.size != checked(array.arrayLength.Value * scalar.size.Value))
                    return "The fixed array extent does not match its exact primitive stride; use its raw carrier constructor.";
                return null;
            }
        }).ToImmutableArray()
    };
}

/// <summary>Plans explicit managed data-carrier conveniences without modifying native field offsets.</summary>
/// <param name="emitSizeConstant">Whether to emit the verified native size.</param>
/// <param name="constructorFields">The ordered nonoverlapping raw fields assigned by a data constructor.</param>
/// <param name="validity">The optional typed sentinel comparison.</param>
public sealed record CSharpRecordPresentationPlan(
    bool emitSizeConstant,
    ImmutableArray<CSharpFieldPlan> constructorFields,
    CSharpRecordValidityPlan? validity
)
{
    /// <summary>Gets independently copied primitive fixed arrays in an additive ReadOnlySpan constructor.</summary>
    public ImmutableArray<CSharpRecordArrayCopyPlan> arrayCopies { get; init; } = [];
    /// <summary>Gets concrete restrictions on additional Span conveniences; the raw-carrier constructor remains available.</summary>
    public ImmutableArray<CSharpRecordArrayCopyOmission> arrayCopyOmissions { get; init; } = [];
}

/// <summary>Plans an exact-length copy into an independently owned inline array field.</summary>
/// <param name="nativeField">The selected original field identity.</param>
/// <param name="fieldName">The checked raw field and constructor parameter name.</param>
/// <param name="elementType">The independently planned primitive element carrier.</param>
/// <param name="length">The complete native element count.</param>
public sealed record CSharpRecordArrayCopyPlan(
    StableId nativeField,
    string fieldName,
    string elementType,
    int length
);

/// <summary>Explains why a selected raw array constructor field does not receive a Span conversion.</summary>
/// <param name="nativeField">The selected original field identity.</param>
/// <param name="fieldName">The checked constructor parameter name.</param>
/// <param name="reason">The concrete storage or semantic restriction.</param>
public sealed record CSharpRecordArrayCopyOmission(
    StableId nativeField,
    string fieldName,
    string reason
);

/// <summary>Plans one scalar validity comparison and an optional compile-time constant assertion.</summary>
/// <param name="propertyName">The public Boolean property.</param>
/// <param name="fieldName">The exact raw field.</param>
/// <param name="fieldType">Its independently planned storage carrier.</param>
/// <param name="invalidExpression">The checked typed sentinel expression.</param>
/// <param name="constantName">The optional private constant requiring consumer-language compile-time evaluation.</param>
public sealed record CSharpRecordValidityPlan(
    string propertyName,
    string fieldName,
    string fieldType,
    string invalidExpression,
    string? constantName
);
