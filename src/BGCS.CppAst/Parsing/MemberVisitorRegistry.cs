using BGCS.CppAst.Model;
using BGCS.CppAst.Parsing.Visitors.MemberVisitors;
using ClangSharp.Interop;

namespace BGCS.CppAst.Parsing;

internal sealed class MemberVisitorRegistry
{
    private readonly CursorVisitorRegistry<MemberVisitor, CppElement?> m_registry = new();

    internal MemberVisitorRegistry()
    {
        m_registry.Register<ClassStructUnionObjCVisitor>();
        m_registry.Register<CXXAccessSpecifierVisitor>();
        m_registry.Register<CXXBaseSpecifierVisitor>();
        m_registry.Register<EnumConstantVisitor>();
        m_registry.Register<EnumDeclMemberVisitor>();
        m_registry.Register<FieldVariableVisitor>();
        m_registry.Register<FlagEnumVisitor>();
        m_registry.Register<FunctionDeclVisitor>();
        m_registry.Register<InclusionDirectiveVisitor>();
        m_registry.Register<LinkageSpecVisitor>();
        m_registry.Register<MacroDefinitionVisitor>();
        m_registry.Register<NamespaceMemberVisitor>();
        m_registry.Register<ObjCClassProtocolRefVisitor>();
        m_registry.Register<ObjCPropertyDeclVisitor>();
        m_registry.Register<TypeAliasDeclVisitor>();
        m_registry.Register<TypedefDeclVisitor>();
        m_registry.Register<TypeRefVisitor>();
        m_registry.Register<UsingDirectiveVisitor>();
        m_registry.Register<MacroExpansionVisitor>();
        m_registry.Register<FirstRefVisitor>();
        m_registry.Register<ObjCIvarDeclVisitor>();
        m_registry.Register<TemplateTypeParameterVisitor>();
        m_registry.Register<UnexposedDeclVisitor>();
    }

    internal T GetVisitor<T>() where T : MemberVisitor => m_registry.GetVisitor<T>();

    internal MemberVisitor? GetVisitor(CXCursorKind kind) => m_registry.GetVisitor(kind);
}
