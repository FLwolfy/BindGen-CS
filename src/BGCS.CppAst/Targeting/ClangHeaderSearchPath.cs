namespace BGCS.CppAst.Targeting;

/// <summary>
/// Retains driver search order and path roles without exposing another compiler's builtin headers.
/// </summary>
internal readonly record struct ClangHeaderSearchPath(
    string path,
    bool isBuiltin,
    bool isFramework
);
