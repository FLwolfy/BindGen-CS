namespace BGCS.Frontend.Clang.Parsing;

using BGCS.Frontend.Clang.Model.Metadata;
using BGCS.Frontend.Clang.Utilities;
using ClangSharp.Interop;

/// <summary>
/// Defines the public class <c>CompilationLoggerBase</c>.
/// </summary>
internal abstract class CompilationLoggerBase
{
    /// <summary>
    /// Gets <c>RootCompilation</c>.
    /// </summary>
    public abstract CppCompilation rootCompilation { get; }

    /// <summary>
    /// Executes public operation <c>Unhandled</c>.
    /// </summary>
    public void Unhandled(CXCursor cursor)
    {
        var cppLocation = cursor.GetSourceLocation();
        this.rootCompilation.diagnostics.Warning($"Unhandled declaration: {cursor.Kind}/{CXUtil.GetCursorSpelling(cursor)}.", cppLocation);
    }

    /// <summary>
    /// Executes public operation <c>WarningUnhandled</c>.
    /// </summary>
    public void WarningUnhandled(
        CXCursor cursor,
        CXCursor parent,
        CXType type
    ) {
        var cppLocation = cursor.GetSourceLocation();
        if (cppLocation.line == 0)
        {
            cppLocation = parent.GetSourceLocation();
        }

        this.rootCompilation.diagnostics.Warning($"The type {cursor.Kind}/`{CXUtil.GetTypeSpelling(type)}` of kind `{CXUtil.GetTypeKindSpelling(type)}` is not supported in `{CXUtil.GetCursorSpelling(parent)}`", cppLocation);
    }

    /// <summary>
    /// Executes public operation <c>WarningUnhandled</c>.
    /// </summary>
    public void WarningUnhandled(
        CXCursor cursor,
        CXCursor parent
    ) {
        var cppLocation = cursor.GetSourceLocation();
        if (cppLocation.line == 0)
        {
            cppLocation = parent.GetSourceLocation();
        }

        this.rootCompilation.diagnostics.Warning($"Unhandled declaration: {cursor.Kind}/{CXUtil.GetCursorSpelling(cursor)} in {CXUtil.GetCursorSpelling(parent)}.", cppLocation);
    }
}
