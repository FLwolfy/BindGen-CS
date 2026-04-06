using System;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

using ClangSharp.Interop;
using BGCS.CppAst.Model.Types;

namespace BGCS.CppAst.Model.Templates;
/// <summary>
/// For c++ specialized template argument
/// </summary>
public class CppTemplateArgument : CppType
{
    /// <summary>
    /// Initializes a new instance of <see cref="CppTemplateArgument"/>.
    /// </summary>
    public CppTemplateArgument(CXCursor cursor, CppType sourceParam, CppType typeArg, bool isSpecializedArgument) : base(cursor, CppTypeKind.TemplateArgumentType)
    {
        SourceParam = sourceParam ?? throw new ArgumentNullException(nameof(sourceParam));
        ArgAsType = typeArg ?? throw new ArgumentNullException(nameof(typeArg));
        ArgKind = CppTemplateArgumentKind.AsType;
        IsSpecializedArgument = isSpecializedArgument;
    }

    /// <summary>
    /// Executes public operation <c>CppTemplateArgument</c>.
    /// </summary>
    public CppTemplateArgument(CXCursor cursor, CppType sourceParam, long intArg) : base(cursor, CppTypeKind.TemplateArgumentType)
    {
        SourceParam = sourceParam ?? throw new ArgumentNullException(nameof(sourceParam));
        ArgAsInteger = intArg;
        ArgKind = CppTemplateArgumentKind.AsInteger;
        IsSpecializedArgument = true;
    }

    /// <summary>
    /// Executes public operation <c>CppTemplateArgument</c>.
    /// </summary>
    public CppTemplateArgument(CXCursor cursor, CppType sourceParam, string? unknownStr) : base(cursor, CppTypeKind.TemplateArgumentType)
    {
        SourceParam = sourceParam ?? throw new ArgumentNullException(nameof(sourceParam));
        ArgAsUnknown = unknownStr;
        ArgKind = CppTemplateArgumentKind.Unknown;
        IsSpecializedArgument = true;
    }

    /// <summary>
    /// Executes public operation <c>CppTemplateArgument</c>.
    /// </summary>
    public CppTemplateArgument(CX_TemplateArgument templateArgument, CppType sourceParam, CppType typeArg, bool isSpecializedArgument) : base(CXCursor.Null, CppTypeKind.TemplateArgumentType)
    {
        TemplateArgument = templateArgument;
        SourceParam = sourceParam ?? throw new ArgumentNullException(nameof(sourceParam));
        ArgAsType = typeArg ?? throw new ArgumentNullException(nameof(typeArg));
        ArgKind = CppTemplateArgumentKind.AsType;
        IsSpecializedArgument = isSpecializedArgument;
    }

    /// <summary>
    /// Executes public operation <c>CppTemplateArgument</c>.
    /// </summary>
    public CppTemplateArgument(CX_TemplateArgument templateArgument, CppType sourceParam, long intArg) : base(CXCursor.Null, CppTypeKind.TemplateArgumentType)
    {
        TemplateArgument = templateArgument;
        SourceParam = sourceParam ?? throw new ArgumentNullException(nameof(sourceParam));
        ArgAsInteger = intArg;
        ArgKind = CppTemplateArgumentKind.AsInteger;
        IsSpecializedArgument = true;
    }

    /// <summary>
    /// Executes public operation <c>CppTemplateArgument</c>.
    /// </summary>
    public CppTemplateArgument(CX_TemplateArgument templateArgument, CppType sourceParam, string? unknownStr) : base(CXCursor.Null, CppTypeKind.TemplateArgumentType)
    {
        TemplateArgument = templateArgument;
        SourceParam = sourceParam ?? throw new ArgumentNullException(nameof(sourceParam));
        ArgAsUnknown = unknownStr;
        ArgKind = CppTemplateArgumentKind.Unknown;
        IsSpecializedArgument = true;
    }

    /// <summary>
    /// Gets or sets <c>TemplateArgument</c>.
    /// </summary>
    public CX_TemplateArgument TemplateArgument { get; set; }

    /// <summary>
    /// Gets <c>ArgKind</c>.
    /// </summary>
    public CppTemplateArgumentKind ArgKind { get; }

    /// <summary>
    /// Gets <c>ArgAsType</c>.
    /// </summary>
    public CppType? ArgAsType { get; }

    /// <summary>
    /// Gets <c>ArgAsInteger</c>.
    /// </summary>
    public long ArgAsInteger { get; }

    /// <summary>
    /// Gets <c>ArgAsUnknown</c>.
    /// </summary>
    public string? ArgAsUnknown { get; }

    /// <summary>
    /// Exposes public member <c>ArgString</c>.
    /// </summary>
    public string ArgString
    {
        get
        {
            return ArgKind switch
            {
                CppTemplateArgumentKind.AsType => ArgAsType?.FullName ?? "?",
                CppTemplateArgumentKind.AsInteger => ArgAsInteger.ToString(),
                CppTemplateArgumentKind.Unknown => ArgAsUnknown ?? "?",
                _ => "?",
            };
        }
    }

    /// <summary>
    /// Gets the default value.
    /// </summary>
    public CppType SourceParam { get; }

    /// <summary>
    /// Gets <c>IsSpecializedArgument</c>.
    /// </summary>
    public bool IsSpecializedArgument { get; }

    /// <inheritdoc />
    public override int SizeOf
    {
        get => 0;
        set => throw new InvalidOperationException("This type does not support SizeOf");
    }

    /// <inheritdoc />
    public override CppType GetCanonicalType() => this;

    /// <inheritdoc />

    /// <inheritdoc />
    public override string ToString() => $"{SourceParam} = {ArgString}";
}
