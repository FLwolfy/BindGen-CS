using System;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

namespace BGCS.CppAst.Model.Types;
using ClangSharp.Interop;
using BGCS.CppAst.Model;

/// <summary>
/// Base class for C++ types.
/// </summary>
public abstract class CppType : CppElement
{
    /// <summary>
    /// Constructor with the specified type kind.
    /// </summary>
    /// <param name="cursor"></param>
    /// <param name="typeKind"></param>
    protected CppType(CXCursor cursor, CppTypeKind typeKind) : base(cursor)
    {
        TypeKind = typeKind;
    }

    /// <summary>
    /// Gets the <see cref="CppTypeKind"/> of this instance.
    /// </summary>
    public CppTypeKind TypeKind { get; }

    public abstract int SizeOf { get; set; }

    /// <summary>
    /// Gets the canonical type of this type instance.
    /// </summary>
    /// <returns>A canonical type of this type instance</returns>
    public abstract CppType GetCanonicalType();

    /// <summary>
    /// We can use this name in exporter to use this type.
    /// </summary>
    public virtual string FullName
    {
        get
        {
            return ToString();
        }
    }
}
