// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
namespace BGCS.CppAst.Model.Types;

using BGCS.CppAst.Model;
using ClangSharp.Interop;

/// <summary>
/// Base class for C++ types.
/// </summary>
public abstract class CppType : CppElement
{
    /// <summary>
    /// Captures the native type category and borrowed declaration cursor.
    /// </summary>
    /// <param name="cursor">
    /// The borrowed Clang cursor, valid only while its compilation remains alive; default creates a synthetic node.
    /// </param>
    /// <param name="typeKind">
    /// The native type shape represented by this mutable projection.
    /// </param>
    protected CppType(
        CXCursor cursor,
        CppTypeKind typeKind
    ) : base(cursor)
    {
        this.typeKind = typeKind;
    }

    /// <summary>
    /// Gets the <see cref = "CppTypeKind"/> of this instance.
    /// </summary>
    public CppTypeKind typeKind { get; }
    /// <summary>
    /// Gets or sets the selected target ABI byte size; derived immutable or computed layouts reject assignment.
    /// </summary>
    public abstract int sizeOf { get; set; }

    /// <summary>
    /// Gets the canonical type of this type instance.
    /// </summary>
    /// <returns>A canonical type of this type instance</returns>
    public abstract CppType GetCanonicalType();
    /// <summary>
    /// We can use this name in exporter to use this type.
    /// </summary>
    public virtual string fullName
    {
        get
        {
            return ToString() ?? string.Empty;
        }
    }
}
