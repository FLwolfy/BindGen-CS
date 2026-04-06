using System;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

using ClangSharp.Interop;
using BGCS.CppAst.Extensions;
using System.Collections.Generic;
using System.Text;

namespace BGCS.CppAst.Model.Types;

/// <summary>
/// A generic type, a type that has a base type and a list of generic type arguments.
/// </summary>
public class CppGenericType : CppType
{
    /// <summary>
    /// Initializes a new instance of <see cref="CppGenericType"/>.
    /// </summary>
    public CppGenericType(CXCursor cursor, CppType baseType) : base(cursor, CppTypeKind.GenericType)
    {
        BaseType = baseType;
        GenericArguments = [];
    }

    /// <summary>
    /// Gets or sets <c>BaseType</c>.
    /// </summary>
    public CppType BaseType { get; set; }

    /// <summary>
    /// Gets <c>GenericArguments</c>.
    /// </summary>
    public List<CppType> GenericArguments { get; }

    /// <summary>
    /// Gets or sets <c>SizeOf</c>.
    /// </summary>
    public override int SizeOf { get; set; }

    /// <summary>
    /// Returns computed data from <c>GetCanonicalType</c>.
    /// </summary>
    public override CppType GetCanonicalType() => this;

    /// <summary>
    /// Executes public operation <c>ToString</c>.
    /// </summary>
    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.Append(BaseType.GetDisplayName());
        if (GenericArguments.Count > 0)
        {
            builder.Append('<');
            for (int i = 0; i < GenericArguments.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }
                builder.Append(GenericArguments[i].GetDisplayName());
            }
            builder.Append('>');
        }
        return builder.ToString();
    }
}
