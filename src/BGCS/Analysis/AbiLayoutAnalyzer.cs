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
        BindingTypeKind kind = cppClass.ClassKind switch
        {
            CppClassKind.Union => BindingTypeKind.Union,
            CppClassKind.Class when !cppClass.IsDefinition => BindingTypeKind.OpaqueHandle,
            _ => BindingTypeKind.Structure
        };
        BindingType bindingType = new(cppClass.FullName, config.GetCsCleanName(cppClass.Name), kind,
            cppClass.SizeOf, cppClass.AlignOf);
        foreach (CppField field in cppClass.Fields)
        {
            IReadOnlyList<int> dimensions = GetArrayDimensions(field.Type);
            BindingTypeReference type = typeAnalyzer.Analyze(GetInnermostElement(field.Type));
            BindingField bindingField = new(field.Name, config.GetFieldName(field.Name), type, field.Offset,
                field.BitOffset, field.IsBitField ? field.BitFieldWidth : 0, dimensions);
            bindingType.Fields.Add(bindingField);
            if (cppClass.ClassKind != CppClassKind.Union && cppClass.SizeOf > 0 && dimensions.All(size => size > 0) &&
                field.Offset + Math.Max(0, field.Type.SizeOf) > cppClass.SizeOf)
                throw new InvalidOperationException($"Field '{cppClass.FullName}::{field.Name}' exceeds the {cppClass.SizeOf}-byte native layout.");
        }
        return bindingType;
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
