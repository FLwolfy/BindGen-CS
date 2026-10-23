// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using System;
using System.Collections.Generic;
using BGCS.Frontend.Clang.Model.Declarations;
using BGCS.Frontend.Clang.Model.Interfaces;
using ClangSharp.Interop;

namespace BGCS.Frontend.Clang.Model.Types;

/// <summary>
/// A type not fully/correctly exposed by the C++ parser.
/// </summary>
/// <remarks>
/// Template parameter type instance are actually exposed with this type.
/// </remarks>
internal sealed class CppUnexposedType : CppType, ICppContainer
{
    /// <summary>
    /// Captures a borrowed native type relationship for attempt-local analysis.
    /// </summary>
    /// <param name="cursor">
    /// The borrowed Clang cursor, valid only while its compilation remains alive; default creates a synthetic node.
    /// </param>
    /// <param name="name">
    /// The non-null native type spelling preserved for later lowering.
    /// </param>
    public CppUnexposedType(
        CXCursor cursor,
        string name
    ) : base(cursor, CppTypeKind.Unexposed)
    {
        this.name = name ?? throw new ArgumentNullException(nameof(name));
    }

    /// <summary>
    /// Full name of the unexposed type
    /// </summary>
    public string name { get; }
    /// <summary>Identifies an unsupported block pointer from its actual compiler type kind.</summary>
    internal bool isBlockPointer { get; init; }
    internal CppFunctionTypeBase? blockSignature { get; init; }
    /// <inheritdoc/>
    public override int sizeOf { get; set; }
    /// <summary>Gets borrowed template argument types without changing their declaration owners.</summary>
    public List<CppType> templateArguments { get; } = [];

    /// <inheritdoc/>
    public override CppType GetCanonicalType() => this;
    /// <inheritdoc/>
    public override string ToString() => this.name;
    /// <summary>
    /// Gets an empty declaration sequence; unexposed template parameters are modeled separately.
    /// </summary>
    public IEnumerable<ICppDeclaration> children => [];
}
