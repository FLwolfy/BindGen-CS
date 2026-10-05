using BGCS.CppAst.Parsing.Visitors;
using ClangSharp.Interop;

namespace BGCS.CppAst.Parsing;

internal sealed class DeclContainerVisitorRegistry
{
    private readonly CursorVisitorRegistry<DeclContainerVisitor, CppContainerContext> m_registry = new();
    private readonly TranslationUnitDeclVisitor m_fallback = new();

    internal DeclContainerVisitorRegistry()
    {
        m_registry.Register<ClassStructDeclVisitor>();
        m_registry.Register<EnumDeclVisitor>();
        m_registry.Register<NamespaceDeclVisitor>();
    }

    internal DeclContainerVisitor GetVisitor(CXCursorKind kind) => m_registry.GetVisitor(kind) ?? m_fallback;
}
