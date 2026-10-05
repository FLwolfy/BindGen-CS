using System;
using System.Collections.Generic;
using System.Text;
using BGCS.CppAst.Collections;
using BGCS.CppAst.Extensions;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Model.Types;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using ClangSharp.Interop;

namespace BGCS.CppAst.Model.Declarations;

/// <summary>
/// An Objective-C block function (e.g `void (^)(int arg1, int arg2)`) or C++ function type (e.g `void (*)(int arg1, int arg2)`)
/// </summary>
public abstract class CppFunctionTypeBase : CppTypeDeclaration
{
    /// <summary>
    /// Creates a callable type with empty owned parameters and a borrowed return type.
    /// </summary>
    /// <param name="cursor">
    /// The borrowed Clang cursor, valid only while its compilation remains alive; default creates a synthetic node.
    /// </param>
    /// <param name="kind">
    /// The native function or Objective-C block category.
    /// </param>
    /// <param name="returnType">
    /// The non-null borrowed native result type.
    /// </param>
    protected CppFunctionTypeBase(
        CXCursor cursor,
        CppTypeKind kind,
        CppType returnType
    ) : base(cursor, kind)
    {
        this.returnType = returnType ?? throw new ArgumentNullException(nameof(returnType));
        this.parameters = new CppContainerList<CppParameter>(this);
    }

    /// <summary>
    /// Gets or sets the calling convention of this function type.
    /// </summary>
    public CppCallingConvention callingConvention { get; set; }
    /// <summary>
    /// Gets or sets the return type of this function type.
    /// </summary>
    public CppType returnType { get; set; }
    /// <summary>
    /// Gets a list of the parameters.
    /// </summary>
    public CppContainerList<CppParameter> parameters { get; }
    /// <inheritdoc/>
    public override int sizeOf { get => 0; set => throw new InvalidOperationException("This type does not support SizeOf"); }
    /// <inheritdoc/>
    public override IEnumerable<ICppDeclaration> children => this.parameters;

    /// <inheritdoc/>
    public override CppType GetCanonicalType() => this;
    /// <inheritdoc/>
    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.Append(this.returnType.GetDisplayName());
        builder.Append(' ');
        // Don't complicate with a virtual methods, hardcode derived cases here
        if (this.typeKind == CppTypeKind.ObjCBlockFunction)
        {
            builder.Append("(^)(");
        }
        else
        {
            builder.Append("(*)(");
        }

        for (var i = 0; i < this.parameters.Count; i++)
        {
            var param = this.parameters[i];
            if (i > 0)
                builder.Append(", ");
            builder.Append(param);
        }

        builder.Append(')');
        return builder.ToString();
    }
}
