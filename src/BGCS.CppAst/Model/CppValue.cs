// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

using ClangSharp.Interop;
using BGCS.CppAst.Model.Declarations;
using System;

namespace BGCS.CppAst.Model;
/// <summary>
/// A C++ default value used to initialize <see cref="CppParameter"/>
/// </summary>
public class CppValue : CppElement
{
    /// <summary>
    /// A default C++ value.
    /// </summary>
    /// <param name="cursor"></param>
    /// <param name="value"></param>
    public CppValue(CXCursor cursor, object value) : base(cursor)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Gets the default value.
    /// </summary>
    public object Value { get; set; }

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
