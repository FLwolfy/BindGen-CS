using System;
using ClangSharp.Interop;
using BGCS.CppAst.Model;
using BGCS.CppAst.Parsing.Visitors.MemberVisitors;

namespace BGCS.CppAst.Parsing;
public static class MemberVisitorRegistry
{
    private static readonly CursorVisitorRegistry<MemberVisitor, CppElement?> registry = new();

    static MemberVisitorRegistry()
    {
        Register<ClassStructUnionObjCVisitor>();
        Register<CXXAccessSpecifierVisitor>();
        Register<CXXBaseSpecifierVisitor>();
        Register<EnumConstantVisitor>();
        Register<EnumDeclMemberVisitor>();
        Register<FieldVariableVisitor>();
        Register<FlagEnumVisitor>();
        Register<FunctionDeclVisitor>();
        Register<InclusionDirectiveVisitor>();
        Register<LinkageSpecVisitor>();
        Register<MacroDefinitionVisitor>();
        Register<NamespaceMemberVisitor>();
        Register<ObjCClassProtocolRefVisitor>();
        Register<ObjCPropertyDeclVisitor>();
        Register<TypeAliasDeclVisitor>();
        Register<TypedefDeclVisitor>();
        Register<TypeRefVisitor>();
        Register<UsingDirectiveVisitor>();
        Register<MacroExpansionVisitor>();
        Register<FirstRefVisitor>();
        Register<ObjCIvarDeclVisitor>();
        Register<TemplateTypeParameterVisitor>();
        Register<UnexposedDeclVisitor>();
    }

    public static T GetVisitor<T>() where T : MemberVisitor => registry.GetVisitor<T>();

    public static MemberVisitor Register<T>() where T : MemberVisitor, new() => registry.Register<T>();

    public static void Register<T>(T visitor) where T : MemberVisitor => registry.Register(visitor);

    public static void Override<T>(MemberVisitor visitor) where T : MemberVisitor => registry.Override<T>(visitor);

    public static void Unregister<T>() where T : MemberVisitor => registry.Unregister<T>();

    public static MemberVisitor? GetVisitor(CXCursorKind kind) => registry.GetVisitorByKind(kind);
}
