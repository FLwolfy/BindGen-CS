using System;
namespace BGCS.CppAst.Parsing;
using ClangSharp.Interop;
using BGCS.CppAst.Parsing.Visitors;

public class DeclContainerVisitorRegistry
{
    private static readonly CursorVisitorRegistry<DeclContainerVisitor, CppContainerContext> registry = new();

    static DeclContainerVisitorRegistry()
    {
        Register<ClassStructDeclVisitor>();
        Register<EnumDeclVisitor>();
        Register<NamespaceDeclVisitor>();
        FallbackVisitor = Register<TranslationUnitDeclVisitor>();
    }

    public static DeclContainerVisitor FallbackVisitor { get; set; }

    public static T GetVisitor<T>() where T : DeclContainerVisitor => registry.GetVisitor<T>();

    public static DeclContainerVisitor Register<T>() where T : DeclContainerVisitor, new() => registry.Register<T>();

    public static void Register<T>(T visitor) where T : DeclContainerVisitor => registry.Register(visitor);

    public static void Override<T>(DeclContainerVisitor visitor) where T : DeclContainerVisitor => registry.Override<T>(visitor);

    public static void Unregister<T>() where T : DeclContainerVisitor => registry.Unregister<T>();

    public static DeclContainerVisitor GetVisitor(CXCursorKind kind) => registry.GetVisitorByKind(kind) ?? FallbackVisitor;
}
