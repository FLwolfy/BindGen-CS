// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.


using ClangSharp.Interop;
using BGCS.CppAst.Collections;
using BGCS.CppAst.Model.Interfaces;
using System;
using System.Collections.Generic;

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
    /// Creates an instance of this type.
    /// </summary>
    /// <param name="cursor"></param>
    /// <param name="name">Fullname of the unexposed type</param>
    public CppUnexposedType(CXCursor cursor, string name) : base(cursor, CppTypeKind.Unexposed)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        TemplateParameters = new CppContainerList<CppType>(this);
    }

    /// <summary>
    /// Full name of the unexposed type
    /// </summary>
    public string Name { get; }

    /// <inheritdoc />
    public override int SizeOf { get; set; }

    /// <inheritdoc />
    public CppContainerList<CppType> TemplateParameters { get; }

    /// <inheritdoc />
    public override CppType GetCanonicalType() => this;

    /// <inheritdoc />
    public override string ToString() => Name;

    public IEnumerable<ICppDeclaration> Children => [];
}
