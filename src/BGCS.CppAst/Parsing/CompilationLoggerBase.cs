using System;
namespace BGCS.CppAst.Parsing;
using ClangSharp.Interop;
using BGCS.CppAst.Model.Metadata;
using BGCS.CppAst.Utilities;

public abstract class CompilationLoggerBase
{
    public abstract CppCompilation RootCompilation { get; }

    public void Unhandled(CXCursor cursor)
    {
        var cppLocation = cursor.GetSourceLocation();
        RootCompilation.Diagnostics.Warning($"Unhandled declaration: {cursor.Kind}/{CXUtil.GetCursorSpelling(cursor)}.", cppLocation);
    }

    public void WarningUnhandled(CXCursor cursor, CXCursor parent, CXType type)
    {
        var cppLocation = cursor.GetSourceLocation();
        if (cppLocation.Line == 0)
        {
            cppLocation = parent.GetSourceLocation();
        }
        RootCompilation.Diagnostics.Warning($"The type {cursor.Kind}/`{CXUtil.GetTypeSpelling(type)}` of kind `{CXUtil.GetTypeKindSpelling(type)}` is not supported in `{CXUtil.GetCursorSpelling(parent)}`", cppLocation);
    }

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
