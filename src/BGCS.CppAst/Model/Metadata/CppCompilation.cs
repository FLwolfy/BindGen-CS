using System;
using BGCS.CppAst.Collections;
using BGCS.CppAst.Diagnostics;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using ClangSharp.Interop;

namespace BGCS.CppAst.Model.Metadata;

/// <summary>
/// The result of a compilation for a sets of C++ files.
/// </summary>
public class CppCompilation : CppGlobalDeclarationContainer, IDisposable
{
    private CXTranslationUnit m_translationUnit;
    /// <summary>
    /// Takes ownership of a live native translation unit and captures its target pointer size.
    /// </summary>
    /// <param name="translationUnit">
    /// The live Clang translation unit transferred to this compilation; the caller must not dispose it separately.
    /// </param>
    public CppCompilation(CXTranslationUnit translationUnit) : base(translationUnit.Cursor)
    {
        this.m_translationUnit = translationUnit;
        this.diagnostics = new();
        this.inputText = string.Empty;
        this.system = new(translationUnit.Cursor);
        using var targetInfo = translationUnit.TargetInfo;
        pointerSize = targetInfo.PointerWidth / 8;
    }

    /// <summary>
    /// Exposes public member <c>translationUnit</c>.
    /// </summary>
    internal CXTranslationUnit translationUnit => this.m_translationUnit;

    /// <summary>
    /// Gets the native target's pointer width in bytes, captured while the translation unit is alive.
    /// </summary>
    public int pointerSize { get; }
    /// <summary>
    /// Gets the attached diagnostic messages.
    /// </summary>
    public CppDiagnosticBag diagnostics { get; }
    /// <summary>
    /// Gets the final input header text used by this compilation.
    /// </summary>
    public string inputText { get; set; }
    /// <summary>
    /// Gets a boolean indicating whether this instance has errors. See <see cref = "diagnostics"/> for more details.
    /// </summary>
    public bool hasErrors => this.diagnostics.hasErrors;
    /// <summary>
    /// Gets all the declarations that are coming from system include folders used by the declarations in this object.
    /// </summary>
    public CppGlobalDeclarationContainer system { get; }

    /// <summary>
    /// Releases the owned translation unit once; all borrowed native cursors, comments, and template arguments then become invalid.
    /// </summary>
    public void Dispose()
    {
        if (this.m_translationUnit.Handle != IntPtr.Zero)
        {
            this.m_translationUnit.Dispose();
            this.m_translationUnit = default;
        }

        GC.SuppressFinalize(this);
    }
}
