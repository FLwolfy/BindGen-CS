namespace BGCS.Analysis;

using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Types;
using BGCS.Intermediate;

/// <summary>
/// Lowers Clang field offsets, array shapes, size, and alignment into binding IR and validates basic layout bounds.
/// </summary>
public sealed class AbiLayoutAnalyzer
{
    private readonly CsCodeGeneratorConfig config;
    private readonly TypeAnalyzer typeAnalyzer;

    public AbiLayoutAnalyzer(CsCodeGeneratorConfig config, TypeAnalyzer typeAnalyzer)
    {
        this.config = config ?? throw new ArgumentNullException(nameof(config));
        this.typeAnalyzer = typeAnalyzer ?? throw new ArgumentNullException(nameof(typeAnalyzer));
    }

    public BindingType Analyze(CppClass cppClass)
    {
        ArgumentNullException.ThrowIfNull(cppClass);
        return Analyze(cppClass, config.GetCsCleanName(cppClass.Name));
    }

    private BindingType Analyze(CppClass cppClass, string managedName)
    {
        BindingTypeKind kind = cppClass.ClassKind switch
        {
            CppClassKind.Union => BindingTypeKind.Union,
            CppClassKind.Class when !cppClass.IsDefinition => BindingTypeKind.OpaqueHandle,
            _ => BindingTypeKind.Structure
        };
        BindingType bindingType = new(cppClass.FullName, managedName, kind,
            cppClass.SizeOf, cppClass.AlignOf);
        for (int index = 0; index < cppClass.Classes.Count; index++)
        {
            CppClass nestedClass = cppClass.Classes[index];
            string nestedManagedName = config.GetCsSubTypeName(cppClass, managedName, nestedClass, index);
            if (nestedClass.IsAnonymous)
                config.TypeConverter.AddAnonymousMapping(nestedClass, nestedManagedName);
            bindingType.NestedTypes.Add(Analyze(nestedClass, nestedManagedName));
        }
        for (int fieldIndex = 0; fieldIndex < cppClass.Fields.Count; fieldIndex++)
        {
            CppField field = cppClass.Fields[fieldIndex];
            IReadOnlyList<int> dimensions = GetArrayDimensions(field.Type);
            BindingTypeReference type = typeAnalyzer.Analyze(GetInnermostElement(field.Type));
            string managedFieldName = string.IsNullOrWhiteSpace(field.Name)
                ? $"AnonymousField{fieldIndex}"
                : config.GetFieldName(field.Name);
            BindingField bindingField = new(field.Name, managedFieldName, type, field.Offset,
                field.BitOffset, field.IsBitField ? field.BitFieldWidth : 0, dimensions, field.IsBitField,
                field.IsBitField && IsSignedBitfield(field.Type));
            bindingType.Fields.Add(bindingField);
            if (cppClass.ClassKind != CppClassKind.Union && cppClass.SizeOf > 0 && dimensions.All(size => size > 0) &&
                field.Offset + Math.Max(0, field.Type.SizeOf) > cppClass.SizeOf)
                throw new InvalidOperationException($"Field '{cppClass.FullName}::{field.Name}' exceeds the {cppClass.SizeOf}-byte native layout.");
        }
        return bindingType;
    }

    private static bool IsSignedBitfield(CppType type)
    {
        return type.GetPrimitiveKind() is CppPrimitiveKind.Char or CppPrimitiveKind.Short or CppPrimitiveKind.Int or
            CppPrimitiveKind.Long or CppPrimitiveKind.LongLong or CppPrimitiveKind.Int128;
    }

    private static IReadOnlyList<int> GetArrayDimensions(CppType type)
    {
        List<int> dimensions = [];
        while (type is CppArrayType array)
        {
            dimensions.Add(array.Size);
            type = array.ElementType;
        }
        return dimensions;
    }

    private static CppType GetInnermostElement(CppType type)
    {
        while (type is CppArrayType array)
            type = array.ElementType;
        return type;
    }
}
