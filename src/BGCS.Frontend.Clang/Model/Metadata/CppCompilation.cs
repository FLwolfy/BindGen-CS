using System;
using System.Collections.Generic;
using BGCS.Frontend.Clang.Model.Types;
using BGCS.Frontend.Clang.Parsing;
using BGCS.Frontend.Clang.Collections;
using BGCS.Frontend.Clang.Diagnostics;
using BGCS.Frontend.Clang.Model.Declarations;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using ClangSharp.Interop;

namespace BGCS.Frontend.Clang.Model.Metadata;

/// <summary>
/// The result of a compilation for a sets of C++ files.
/// </summary>
internal class CppCompilation : CppGlobalDeclarationContainer, IDisposable
{
    private CXTranslationUnit m_translationUnit;
    internal Dictionary<CppPrimitiveType, PrimitiveTypeFacts> primitiveFacts { get; } = [];
    internal Dictionary<CppType, NativeStorageFacts> storageFacts { get; } = new(ReferenceEqualityComparer.Instance);
    internal Dictionary<CppClass, bool?> defaultConstructionFacts { get; } = new(ReferenceEqualityComparer.Instance);
    internal Dictionary<CppClass, (bool? StandardLayout, bool? TriviallyCopyable)> recordStorageTraits { get; } = new(ReferenceEqualityComparer.Instance);
    internal Dictionary<CppFunction, bool> callableExceptionFacts { get; } = new(ReferenceEqualityComparer.Instance);
    internal Dictionary<CppFunction, BGCS.Contracts.Native.NativeCallableAddressability> callableAddressabilityFacts { get; } = new(ReferenceEqualityComparer.Instance);
    internal Dictionary<BGCS.Frontend.Clang.Model.CppElement, BGCS.Contracts.Native.NativeScalarConstant> constantFacts { get; } = new(ReferenceEqualityComparer.Instance);
    internal Dictionary<BGCS.Frontend.Clang.Model.CppMacro, bool> macroActiveFacts { get; } = new(ReferenceEqualityComparer.Instance);
    internal Dictionary<BGCS.Frontend.Clang.Model.CppMacro, CppFunction> macroFunctionAliases { get; } = new(ReferenceEqualityComparer.Instance);
    internal Dictionary<BGCS.Frontend.Clang.Model.CppElement, BGCS.Contracts.Native.NativeStringConstant> stringConstantFacts { get; } = new(ReferenceEqualityComparer.Instance);
    internal Dictionary<BGCS.Frontend.Clang.Model.CppElement, CppFunction> functionReferenceFacts { get; } = new(ReferenceEqualityComparer.Instance);
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
