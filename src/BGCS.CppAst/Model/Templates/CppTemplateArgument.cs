using System;
using System.Globalization;
using BGCS.CppAst.Model.Types;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using ClangSharp.Interop;

namespace BGCS.CppAst.Model.Templates;

/// <summary>
/// Represents a type, integral value, or unresolved spelling supplied to a native template parameter.
/// </summary>
public class CppTemplateArgument : CppType
{
    /// <summary>
    /// Retains the parameter binding and its native or unresolved specialization value.
    /// </summary>
    /// <param name="cursor">
    /// The borrowed Clang declaration cursor; its translation unit must outlive native cursor access.
    /// </param>
    /// <param name="sourceParam">
    /// The non-null template parameter type to which this argument is bound.
    /// </param>
    /// <param name="typeArg">
    /// The non-null native type supplied for the parameter.
    /// </param>
    /// <param name="isSpecializedArgument">
    /// Whether the supplied type is a specialization value rather than a dependent parameter.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// The source parameter or a supplied type argument is null.
    /// </exception>
    public CppTemplateArgument(
        CXCursor cursor,
        CppType sourceParam,
        CppType typeArg,
        bool isSpecializedArgument
    ) : base(cursor, CppTypeKind.TemplateArgumentType)
    {
        this.sourceParam = sourceParam ?? throw new ArgumentNullException(nameof(sourceParam));
        this.argAsType = typeArg ?? throw new ArgumentNullException(nameof(typeArg));
        this.argKind = CppTemplateArgumentKind.AsType;
        this.isSpecializedArgument = isSpecializedArgument;
    }

    /// <summary>
    /// Retains the parameter binding and its native or unresolved specialization value.
    /// </summary>
    /// <param name="cursor">
    /// The borrowed Clang declaration cursor; its translation unit must outlive native cursor access.
    /// </param>
    /// <param name="sourceParam">
    /// The non-null template parameter type to which this argument is bound.
    /// </param>
    /// <param name="intArg">
    /// The signed integral specialization value.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// The source parameter is null.
    /// </exception>
    public CppTemplateArgument(
        CXCursor cursor,
        CppType sourceParam,
        long intArg
    ) : base(cursor, CppTypeKind.TemplateArgumentType)
    {
        this.sourceParam = sourceParam ?? throw new ArgumentNullException(nameof(sourceParam));
        this.argAsInteger = intArg;
        this.argKind = CppTemplateArgumentKind.AsInteger;
        this.isSpecializedArgument = true;
    }

    /// <summary>
    /// Retains the parameter binding and its native or unresolved specialization value.
    /// </summary>
    /// <param name="cursor">
    /// The borrowed Clang declaration cursor; its translation unit must outlive native cursor access.
    /// </param>
    /// <param name="sourceParam">
    /// The non-null template parameter type to which this argument is bound.
    /// </param>
    /// <param name="unknownStr">
    /// The unresolved argument spelling, or null when unavailable.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// The source parameter is null.
    /// </exception>
    public CppTemplateArgument(
        CXCursor cursor,
        CppType sourceParam,
        string? unknownStr
    ) : base(cursor, CppTypeKind.TemplateArgumentType)
    {
        this.sourceParam = sourceParam ?? throw new ArgumentNullException(nameof(sourceParam));
        this.argAsUnknown = unknownStr;
        this.argKind = CppTemplateArgumentKind.Unknown;
        this.isSpecializedArgument = true;
    }

    /// <summary>
    /// Retains the parameter binding and its native or unresolved specialization value.
    /// </summary>
    /// <param name="templateArgument">
    /// The borrowed Clang template-argument handle; its translation unit retains ownership.
    /// </param>
    /// <param name="sourceParam">
    /// The non-null template parameter type to which this argument is bound.
    /// </param>
    /// <param name="typeArg">
    /// The non-null native type supplied for the parameter.
    /// </param>
    /// <param name="isSpecializedArgument">
    /// Whether the supplied type is a specialization value rather than a dependent parameter.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// The source parameter or a supplied type argument is null.
    /// </exception>
    public CppTemplateArgument(
        CX_TemplateArgument templateArgument,
        CppType sourceParam,
        CppType typeArg,
        bool isSpecializedArgument
    ) : base(CXCursor.Null, CppTypeKind.TemplateArgumentType)
    {
        this.templateArgument = templateArgument;
        this.sourceParam = sourceParam ?? throw new ArgumentNullException(nameof(sourceParam));
        this.argAsType = typeArg ?? throw new ArgumentNullException(nameof(typeArg));
        this.argKind = CppTemplateArgumentKind.AsType;
        this.isSpecializedArgument = isSpecializedArgument;
    }

    /// <summary>
    /// Retains the parameter binding and its native or unresolved specialization value.
    /// </summary>
    /// <param name="templateArgument">
    /// The borrowed Clang template-argument handle; its translation unit retains ownership.
    /// </param>
    /// <param name="sourceParam">
    /// The non-null template parameter type to which this argument is bound.
    /// </param>
    /// <param name="intArg">
    /// The signed integral specialization value.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// The source parameter is null.
    /// </exception>
    public CppTemplateArgument(
        CX_TemplateArgument templateArgument,
        CppType sourceParam,
        long intArg
    ) : base(CXCursor.Null, CppTypeKind.TemplateArgumentType)
    {
        this.templateArgument = templateArgument;
        this.sourceParam = sourceParam ?? throw new ArgumentNullException(nameof(sourceParam));
        this.argAsInteger = intArg;
        this.argKind = CppTemplateArgumentKind.AsInteger;
        this.isSpecializedArgument = true;
    }

    /// <summary>
    /// Retains the parameter binding and its native or unresolved specialization value.
    /// </summary>
    /// <param name="templateArgument">
    /// The borrowed Clang template-argument handle; its translation unit retains ownership.
    /// </param>
    /// <param name="sourceParam">
    /// The non-null template parameter type to which this argument is bound.
    /// </param>
    /// <param name="unknownStr">
    /// The unresolved argument spelling, or null when unavailable.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// The source parameter is null.
    /// </exception>
    public CppTemplateArgument(
        CX_TemplateArgument templateArgument,
        CppType sourceParam,
        string? unknownStr
    ) : base(CXCursor.Null, CppTypeKind.TemplateArgumentType)
    {
        this.templateArgument = templateArgument;
        this.sourceParam = sourceParam ?? throw new ArgumentNullException(nameof(sourceParam));
        this.argAsUnknown = unknownStr;
        this.argKind = CppTemplateArgumentKind.Unknown;
        this.isSpecializedArgument = true;
    }

    /// <summary>
    /// Gets or sets the borrowed Clang specialization handle, valid only while its translation unit remains alive.
    /// </summary>
    public CX_TemplateArgument templateArgument { get; set; }
    /// <summary>
    /// Gets which specialization representation carries this argument.
    /// </summary>
    public CppTemplateArgumentKind argKind { get; }
    /// <summary>
    /// Gets the native type argument, or null for integral and unresolved representations.
    /// </summary>
    public CppType? argAsType { get; }
    /// <summary>
    /// Gets the integral argument value; only meaningful when the argument kind is integral.
    /// </summary>
    public long argAsInteger { get; }
    /// <summary>
    /// Gets unresolved argument text, or null when no unresolved spelling is retained.
    /// </summary>
    public string? argAsUnknown { get; }

    /// <summary>
    /// Gets the type spelling, invariant integral spelling, or unresolved text; absent spellings display as a question mark.
    /// </summary>
    public string argString
    {
        get
        {
            return this.argKind switch
            {
                CppTemplateArgumentKind.AsType => this.argAsType?.fullName ?? "?",
                CppTemplateArgumentKind.AsInteger => this.argAsInteger.ToString(CultureInfo.InvariantCulture),
                CppTemplateArgumentKind.Unknown => this.argAsUnknown ?? "?",
                _ => "?",
            };
        }
    }

    /// <summary>
    /// Gets the retained template parameter type associated with this argument.
    /// </summary>
    public CppType sourceParam { get; }
    /// <summary>
    /// Gets whether this binding represents a specialization value instead of a dependent argument.
    /// </summary>
    public bool isSpecializedArgument { get; }
    /// <inheritdoc/>
    public override int sizeOf { get => 0; set => throw new InvalidOperationException("This type does not support SizeOf"); }

    /// <inheritdoc/>
    public override CppType GetCanonicalType() => this;
    /// <inheritdoc/>
    public override string ToString() => $"{this.sourceParam} = {this.argString}";
}
