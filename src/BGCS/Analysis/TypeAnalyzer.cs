namespace BGCS.Analysis;

using BGCS.Conversion;
using BGCS.CppAst.Model.Types;
using BGCS.Intermediate;

/// <summary>
/// Converts C/C++ AST types into target-ABI-aware intermediate type references.
/// </summary>
public sealed class TypeAnalyzer
{
    private readonly CsCodeGeneratorConfig config;

    public TypeAnalyzer(CsCodeGeneratorConfig config)
    {
        this.config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public BindingTypeReference Analyze(CppType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        int pointerDepth = 0;
        bool isConst = false;
        CppType current = type;
        while (true)
        {
            switch (current)
            {
                case CppPointerType pointer:
                    pointerDepth++;
                    current = pointer.ElementType;
                    continue;
                case CppReferenceType reference:
                    pointerDepth++;
                    current = reference.ElementType;
                    continue;
                case CppQualifiedType qualified:
                    isConst |= qualified.Qualifier == CppTypeQualifier.Const;
                    current = qualified.ElementType;
                    continue;
                case CppArrayType array:
                    current = array.ElementType;
                    continue;
            }
            break;
        }

        string managedName = config.TypeConverter.Convert(type, CsTypeStyle.Raw);
        if (current is CppPrimitiveType { Kind: CppPrimitiveKind.Bool })
            managedName = config.GetBoolType() + new string('*', pointerDepth);
        return new(type.GetDisplayName(), managedName, pointerDepth, isConst, type.SizeOf);
    }
}
