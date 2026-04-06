using System;
namespace BGCS.CppAst.Parsing;
using ClangSharp.Interop;
using BGCS.CppAst.Model.Metadata;
using BGCS.CppAst.Utilities;

/// <summary>
/// Defines the public class <c>CompilationLoggerBase</c>.
/// </summary>
public abstract class CompilationLoggerBase
{
    /// <summary>
    /// Gets <c>RootCompilation</c>.
    /// </summary>
    public abstract CppCompilation RootCompilation { get; }

    /// <summary>
    /// Executes public operation <c>Unhandled</c>.
    /// </summary>
    public void Unhandled(CXCursor cursor)
    {
        var cppLocation = cursor.GetSourceLocation();
        RootCompilation.Diagnostics.Warning($"Unhandled declaration: {cursor.Kind}/{CXUtil.GetCursorSpelling(cursor)}.", cppLocation);
    }

    /// <summary>
    /// Executes public operation <c>WarningUnhandled</c>.
    /// </summary>
    public void WarningUnhandled(CXCursor cursor, CXCursor parent, CXType type)
    {
        var cppLocation = cursor.GetSourceLocation();
        if (cppLocation.Line == 0)
        {
            cppLocation = parent.GetSourceLocation();
        }
        RootCompilation.Diagnostics.Warning($"The type {cursor.Kind}/`{CXUtil.GetTypeSpelling(type)}` of kind `{CXUtil.GetTypeKindSpelling(type)}` is not supported in `{CXUtil.GetCursorSpelling(parent)}`", cppLocation);
    }

    /// <summary>
    /// Executes public operation <c>WarningUnhandled</c>.
    /// </summary>
    public void WarningUnhandled(CXCursor cursor, CXCursor parent)
    {
        var cppLocation = cursor.GetSourceLocation();
        if (cppLocation.Line == 0)
        {
            cppLocation = parent.GetSourceLocation();
        }
        RootCompilation.Diagnostics.Warning($"Unhandled declaration: {cursor.Kind}/{CXUtil.GetCursorSpelling(cursor)} in {CXUtil.GetCursorSpelling(parent)}.", cppLocation);
    }
}
