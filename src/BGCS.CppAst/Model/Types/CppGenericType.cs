using System.Collections.Generic;
using System.Text;
using BGCS.CppAst.Extensions;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using ClangSharp.Interop;

namespace BGCS.CppAst.Model.Types;

/// <summary>
/// Represents a mutable native generic type projection and its ordered borrowed type arguments.
/// </summary>
public class CppGenericType : CppType
{
    /// <summary>
    /// Captures the borrowed native type and its specialization or array shape.
    /// </summary>
    /// <param name="cursor">
    /// The borrowed Clang cursor, valid only while its owning compilation remains alive; default creates a synthetic node.
    /// </param>
    /// <param name="baseType">
    /// The borrowed unspecialized type; concrete arguments are appended to genericArguments.
    /// </param>
    public CppGenericType(
        CXCursor cursor,
        CppType baseType
    ) : base(cursor, CppTypeKind.GenericType)
    {
        this.baseType = baseType;
        this.genericArguments = [];
    }

    /// <summary>
    /// Gets or sets the borrowed unspecialized native type.
    /// </summary>
    public CppType baseType { get; set; }
    /// <summary>
    /// Gets the mutable ordered borrowed native type arguments.
    /// </summary>
    public List<CppType> genericArguments { get; }
    /// <summary>
    /// Gets or sets the byte size captured for this instantiated native type.
    /// </summary>
    public override int sizeOf { get; set; }

    /// <summary>
    /// Returns this explicit generic projection without lowering its argument structure.
    /// </summary>
    /// <returns>
    /// This same mutable generic type node, borrowed from its owning compilation.
    /// </returns>
    public override CppType GetCanonicalType() => this;
    /// <summary>
    /// Formats the native base type and current ordered generic argument display names.
    /// </summary>
    /// <returns>
    /// The diagnostic native specialization spelling, not a persisted binding identity.
    /// </returns>
    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.Append(this.baseType.GetDisplayName());
        if (this.genericArguments.Count > 0)
        {
            builder.Append('<');
            for (int i = 0; i < this.genericArguments.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(this.genericArguments[i].GetDisplayName());
            }

            builder.Append('>');
        }

        return builder.ToString();
    }
}
