using System;
using System.Collections.Generic;
using System.Linq;
using BGCS.Configuration;
using BGCS.Conversion;

namespace BGCS.Analysis;

using BGCS.Configuration.Mapping;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Types;
using BGCS.Intermediate;

/// <summary>
/// Lowers Clang field offsets, array shapes, size, and alignment into binding IR and validates basic layout bounds.
/// </summary>
public sealed class AbiLayoutAnalyzer
{
    private readonly CsCodeGeneratorConfig m_config;
    private readonly TypeAnalyzer m_typeAnalyzer;

    /// <summary>
    /// Creates a record-layout analyzer using the shared type analysis policy.
    /// </summary>
    /// <param name="config">Configuration borrowed for this generation attempt.</param>
    /// <param name="typeAnalyzer">Type analyzer used to resolve fields and nested declarations.</param>
    public AbiLayoutAnalyzer(
        CsCodeGeneratorConfig config,
        TypeAnalyzer typeAnalyzer
    ) {
        this.m_config = config ?? throw new ArgumentNullException(nameof(config));
        this.m_typeAnalyzer = typeAnalyzer ?? throw new ArgumentNullException(nameof(typeAnalyzer));
    }

    /// <summary>
    /// Freezes a native record layout and reports inconsistent offsets or unsupported shapes.
    /// </summary>
    /// <param name="cppClass">Record definition from a live compilation.</param>
    /// <returns>An immutable record representation containing fields, sizes, alignment and layout diagnostics.</returns>
    public BindingType Analyze(CppClass cppClass)
    {
        ArgumentNullException.ThrowIfNull(cppClass);
        return Analyze(cppClass, this.m_config.GetManagedTypeName(cppClass.name));
    }

    internal BindingType Analyze(
        CppClass cppClass,
        string managedName
    ) {
        BindingTypeKind kind = !cppClass.isDefinition ? BindingTypeKind.OpaqueHandle : cppClass.classKind switch
        {
            CppClassKind.Union => BindingTypeKind.Union,
            _ => BindingTypeKind.Structure
        };
        BindingTypeBuilder bindingType = new(cppClass.fullName, managedName, kind, cppClass.sizeOf, cppClass.alignOf);
        for (int index = 0; index < cppClass.classes.Count; index++)
        {
            CppClass nestedClass = cppClass.classes[index];
            string nestedManagedName = this.m_config.GetCsSubTypeName(cppClass, managedName, nestedClass, index);
            if (nestedClass.isAnonymous)
                this.m_config.typeConverter.AddAnonymousMapping(nestedClass, nestedManagedName);
            bindingType.nestedTypes.Add(Analyze(nestedClass, nestedManagedName));
        }

        for (int fieldIndex = 0; fieldIndex < cppClass.fields.Count; fieldIndex++)
        {
            CppField field = cppClass.fields[fieldIndex];
            IReadOnlyList<int> dimensions = GetArrayDimensions(field.type);
            BindingTypeReference type = this.m_typeAnalyzer.Analyze(GetInnermostElement(field.type));
            TypeFieldMapping? fieldMapping = this.m_config.GetTypeMapping(cppClass.name)?.GetFieldMapping(field.name);
            string managedFieldName = string.IsNullOrWhiteSpace(field.name) ? $"AnonymousField{fieldIndex}" : this.m_config.GetFieldName(field.name, fieldMapping?.displayName);
            BindingField bindingField = new(field.name, managedFieldName, type, field.offset, field.bitOffset, field.isBitField ? field.bitFieldWidth : 0, dimensions, field.isBitField, field.isBitField && IsSignedBitfield(field.type))
            {
                comment = fieldMapping?.comment
            };
            bindingType.fields.Add(bindingField);
            long fieldEnd = field.isBitField
                ? checked((field.bitOffset + field.bitFieldWidth + 7) / 8)
                : checked(field.offset + Math.Max(0, field.type.sizeOf));
            if (cppClass.sizeOf > 0 && dimensions.All(size => size > 0) && fieldEnd > cppClass.sizeOf)
                throw new InvalidOperationException($"Field '{cppClass.fullName}::{field.name}' exceeds the {cppClass.sizeOf}-byte native layout.");
        }

        StructValidityMapping? validity = this.m_config.GetTypeMapping(cppClass.name)?.validity;
        if (validity != null)
        {
            BindingField? field = bindingType.fields.FirstOrDefault(candidate => string.Equals(candidate.nativeName, validity.fieldName, StringComparison.Ordinal));
            if (field == null)
                throw new InvalidOperationException($"Validity mapping for '{cppClass.fullName}' references missing field '{validity.fieldName}'.");
            bindingType.validity = new(field.managedName, validity.invalidValue, this.m_config.GetCsCleanName(validity.propertyName));
        }

        return bindingType;
    }

    private static bool IsSignedBitfield(CppType type)
    {
        while (type is CppTypedef or CppQualifiedType)
            type = type is CppTypedef alias ? alias.elementType : ((CppQualifiedType)type).elementType;
        if (type is CppEnum enumeration)
            type = enumeration.integerType;
        return type.GetPrimitiveKind() is CppPrimitiveKind.Char or CppPrimitiveKind.Short or CppPrimitiveKind.Int or CppPrimitiveKind.Long or CppPrimitiveKind.LongLong or CppPrimitiveKind.Int128;
    }

    private static IReadOnlyList<int> GetArrayDimensions(CppType type)
    {
        List<int> dimensions = [];
        while (type is CppArrayType array)
        {
            dimensions.Add(array.size);
            type = array.elementType;
        }

        return dimensions;
    }

    private static CppType GetInnermostElement(CppType type)
    {
        while (type is CppArrayType array)
            type = array.elementType;
        return type;
    }
}
