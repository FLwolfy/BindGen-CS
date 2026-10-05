// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using System;
using System.Collections.Generic;
using BGCS.CppAst.Collections;
using BGCS.CppAst.Model.Interfaces;
using ClangSharp.Interop;

namespace BGCS.CppAst.Model.Types;

/// <summary>
/// A type not fully/correctly exposed by the C++ parser.
/// </summary>
/// <remarks>
/// Template parameter type instance are actually exposed with this type.
/// </remarks>
public sealed class CppUnexposedType : CppType, ICppTemplateOwner, ICppContainer
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
        this.templateParameters = new CppContainerList<CppType>(this);
    }

    /// <summary>
    /// Full name of the unexposed type
    /// </summary>
    public string name { get; }
    /// <inheritdoc/>
    public override int sizeOf { get; set; }
    /// <inheritdoc/>
    public CppContainerList<CppType> templateParameters { get; }

    /// <inheritdoc/>
    public override CppType GetCanonicalType() => this;
    /// <inheritdoc/>
    public override string ToString() => this.name;
    /// <summary>
    /// Gets an empty declaration sequence; unexposed template parameters are modeled separately.
    /// </summary>
    public IEnumerable<ICppDeclaration> children => [];
}
