using System;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

using ClangSharp.Interop;
using BGCS.CppAst.Collections;
using BGCS.CppAst.Diagnostics;

namespace BGCS.CppAst.Model.Metadata;
/// <summary>
/// The result of a compilation for a sets of C++ files.
/// </summary>
public class CppCompilation : CppGlobalDeclarationContainer, IDisposable
{
    private CXTranslationUnit translationUnit;

    /// <summary>
    /// Constructor of this object.
    /// </summary>
    public CppCompilation(CXTranslationUnit translationUnit) : base(translationUnit.Cursor)
    {
        this.translationUnit = translationUnit;
        Diagnostics = new();

        System = new(translationUnit.Cursor);
    }

    public CXTranslationUnit TranslationUnit => translationUnit;

    /// <summary>
    /// Gets the attached diagnostic messages.
    /// </summary>
    public CppDiagnosticBag Diagnostics { get; }

    /// <summary>
    /// Gets the final input header text used by this compilation.
    /// </summary>
    public string InputText { get; set; }

    /// <summary>
    /// Gets a boolean indicating whether this instance has errors. See <see cref="Diagnostics"/> for more details.
    /// </summary>
    public bool HasErrors => Diagnostics.HasErrors;

    /// <summary>
    /// Gets all the declarations that are coming from system include folders used by the declarations in this object.
    /// </summary>
    public CppGlobalDeclarationContainer System { get; }

    public void Dispose()
    {
        if (translationUnit.Handle != IntPtr.Zero)
        {
            translationUnit.Dispose();
            translationUnit = default;
        }

        GC.SuppressFinalize(this);
    }
}
