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
    public CppGenericType(CXCursor cursor, CppType baseType) : base(cursor, CppTypeKind.GenericType)
    {
        BaseType = baseType;
        GenericArguments = [];
    }

    public CppType BaseType { get; set; }

    public List<CppType> GenericArguments { get; }

    public override int SizeOf { get; set; }

    public override CppType GetCanonicalType() => this;

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