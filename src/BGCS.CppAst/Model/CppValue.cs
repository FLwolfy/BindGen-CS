// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using System;
using BGCS.CppAst.Model.Declarations;
using ClangSharp.Interop;

namespace BGCS.CppAst.Model;

/// <summary>
/// A C++ default value used to initialize <see cref = "CppParameter"/>
/// </summary>
public class CppValue : CppElement
{
    /// <summary>
    /// Creates a mutable native constant value projection borrowing its compilation lifetime.
    /// </summary>
    /// <param name="cursor">
    /// The borrowed Clang cursor, valid only while its owning compilation remains alive; default creates a synthetic node.
    /// </param>
    /// <param name="value">
    /// The non-null managed value retained without copying.
    /// </param>
    public CppValue(
        CXCursor cursor,
        object value
    ) : base(cursor)
    {
        this.value = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Gets or sets the managed evaluated value retained without copying.
    /// </summary>
    public object value { get; set; }

    /// <inheritdoc/>
    public override string ToString() => this.value.ToString() ?? string.Empty;
}
