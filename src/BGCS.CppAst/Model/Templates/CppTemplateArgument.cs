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
    public CppTemplateArgument(CXCursor cursor, CppType sourceParam, CppType typeArg, bool isSpecializedArgument) : base(cursor, CppTypeKind.TemplateArgumentType)
    {
        SourceParam = sourceParam ?? throw new ArgumentNullException(nameof(sourceParam));
        ArgAsType = typeArg ?? throw new ArgumentNullException(nameof(typeArg));
        ArgKind = CppTemplateArgumentKind.AsType;
        IsSpecializedArgument = isSpecializedArgument;
    }

    public CppTemplateArgument(CXCursor cursor, CppType sourceParam, long intArg) : base(cursor, CppTypeKind.TemplateArgumentType)
    {
        SourceParam = sourceParam ?? throw new ArgumentNullException(nameof(sourceParam));
        ArgAsInteger = intArg;
        ArgKind = CppTemplateArgumentKind.AsInteger;
        IsSpecializedArgument = true;
    }

    public CppTemplateArgument(CXCursor cursor, CppType sourceParam, string? unknownStr) : base(cursor, CppTypeKind.TemplateArgumentType)
    {
        SourceParam = sourceParam ?? throw new ArgumentNullException(nameof(sourceParam));
        ArgAsUnknown = unknownStr;
        ArgKind = CppTemplateArgumentKind.Unknown;
        IsSpecializedArgument = true;
    }

    public CppTemplateArgument(CX_TemplateArgument templateArgument, CppType sourceParam, CppType typeArg, bool isSpecializedArgument) : base(CXCursor.Null, CppTypeKind.TemplateArgumentType)
    {
        TemplateArgument = templateArgument;
        SourceParam = sourceParam ?? throw new ArgumentNullException(nameof(sourceParam));
        ArgAsType = typeArg ?? throw new ArgumentNullException(nameof(typeArg));
        ArgKind = CppTemplateArgumentKind.AsType;
        IsSpecializedArgument = isSpecializedArgument;
    }

    public CppTemplateArgument(CX_TemplateArgument templateArgument, CppType sourceParam, long intArg) : base(CXCursor.Null, CppTypeKind.TemplateArgumentType)
    {
        TemplateArgument = templateArgument;
        SourceParam = sourceParam ?? throw new ArgumentNullException(nameof(sourceParam));
        ArgAsInteger = intArg;
        ArgKind = CppTemplateArgumentKind.AsInteger;
        IsSpecializedArgument = true;
    }

    public CppTemplateArgument(CX_TemplateArgument templateArgument, CppType sourceParam, string? unknownStr) : base(CXCursor.Null, CppTypeKind.TemplateArgumentType)
    {
        TemplateArgument = templateArgument;
        SourceParam = sourceParam ?? throw new ArgumentNullException(nameof(sourceParam));
        ArgAsUnknown = unknownStr;
        ArgKind = CppTemplateArgumentKind.Unknown;
        IsSpecializedArgument = true;
    }

    public CX_TemplateArgument TemplateArgument { get; set; }

    public CppTemplateArgumentKind ArgKind { get; }

    public CppType? ArgAsType { get; }

    public long ArgAsInteger { get; }

    public string? ArgAsUnknown { get; }

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
