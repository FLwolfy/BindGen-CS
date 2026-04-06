using System;
namespace BGCS.CppAst.Parsing;
using ClangSharp.Interop;
using BGCS.CppAst.Parsing.Visitors;

/// <summary>
/// Defines the public class <c>DeclContainerVisitorRegistry</c>.
/// </summary>
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

    /// <summary>
    /// Gets or sets <c>FallbackVisitor</c>.
    /// </summary>
    public static DeclContainerVisitor FallbackVisitor { get; set; }

    /// <summary>
    /// Returns computed data from <c>GetVisitor</c>.
    /// </summary>
    public static T GetVisitor<T>() where T : DeclContainerVisitor => registry.GetVisitor<T>();

    /// <summary>
    /// Executes public operation <c>Register</c>.
    /// </summary>
    public static DeclContainerVisitor Register<T>() where T : DeclContainerVisitor, new() => registry.Register<T>();

    /// <summary>
    /// Executes public operation <c>Register</c>.
    /// </summary>
    public static void Register<T>(T visitor) where T : DeclContainerVisitor => registry.Register(visitor);

    /// <summary>
    /// Executes public operation <c>Override</c>.
    /// </summary>
    public static void Override<T>(DeclContainerVisitor visitor) where T : DeclContainerVisitor => registry.Override<T>(visitor);

    /// <summary>
    /// Executes public operation <c>Unregister</c>.
    /// </summary>
    public static void Unregister<T>() where T : DeclContainerVisitor => registry.Unregister<T>();

    /// <summary>
    /// Returns computed data from <c>GetVisitor</c>.
    /// </summary>
    public static DeclContainerVisitor GetVisitor(CXCursorKind kind) => registry.GetVisitorByKind(kind) ?? FallbackVisitor;
}
