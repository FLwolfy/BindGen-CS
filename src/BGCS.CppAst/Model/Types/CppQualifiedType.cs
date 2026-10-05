using BGCS.CppAst.Extensions;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using ClangSharp.Interop;

namespace BGCS.CppAst.Model.Types;

/// <summary>
/// A C++ qualified type (e.g `const int`)
/// </summary>
public sealed class CppQualifiedType : CppTypeWithElementType
{
    /// <summary>
    /// Captures a borrowed native type relationship for attempt-local analysis.
    /// </summary>
    /// <param name="cursor">
    /// The borrowed Clang cursor, valid only while its compilation remains alive; default creates a synthetic node.
    /// </param>
    /// <param name="qualifier">
    /// The native qualifier applied to the contained type.
    /// </param>
    /// <param name="elementType">
    /// The non-null borrowed type receiving the qualifier.
    /// </param>
    public CppQualifiedType(
        CXCursor cursor,
        CppTypeQualifier qualifier,
        CppType elementType
    ) : base(cursor, CppTypeKind.Qualified, elementType)
    {
        this.qualifier = qualifier;
        this.sizeOf = elementType.sizeOf;
    }

    /// <summary>
    /// Gets the qualifier
    /// </summary>
    public CppTypeQualifier qualifier { get; }

    /// <inheritdoc/>
    public override CppType GetCanonicalType()
    {
        var elementTypeCanonical = this.elementType.GetCanonicalType();
        return ReferenceEquals(elementTypeCanonical, this.elementType) ? this : new CppQualifiedType(this.cursor.CanonicalCursor, this.qualifier, elementTypeCanonical);
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"{this.elementType.GetDisplayName()} {this.qualifier.ToString().ToLowerInvariant()}";
    }
}
