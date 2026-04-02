using System;
using System.Collections.Generic;
using System.IO;
namespace BGCS.CppAst.Parsing;
using ClangSharp.Interop;
using BGCS.CppAst.Extensions;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Expressions;
using BGCS.CppAst.Model.Metadata;
using BGCS.CppAst.Model.Types;
using BGCS.CppAst.Utilities;
using System.Text;

public static class Extensions
{
    public static CppVisibility ToVisibility(this CX_CXXAccessSpecifier accessSpecifier)
    {
        return accessSpecifier switch
        {
            CX_CXXAccessSpecifier.CX_CXXProtected => CppVisibility.Protected,
            CX_CXXAccessSpecifier.CX_CXXPrivate => CppVisibility.Private,
            CX_CXXAccessSpecifier.CX_CXXPublic => CppVisibility.Public,
            _ => CppVisibility.Default,
        };
    }

    public static CppVisibility GetVisibility(this in CXCursor cursor)
    {
        return cursor.CXXAccessSpecifier.ToVisibility();
    }

    public static CppStorageQualifier GetStorageQualifier(this in CXCursor cursor)
    {
        return cursor.StorageClass switch
        {
            CX_StorageClass.CX_SC_Extern or CX_StorageClass.CX_SC_PrivateExtern => CppStorageQualifier.Extern,
            CX_StorageClass.CX_SC_Static => CppStorageQualifier.Static,
            _ => CppStorageQualifier.None,
        };
    }

    public static bool IsAnonymousTypeUsed(this CppType type, CppType anonymousType)
    {
        return IsAnonymousTypeUsed(type, anonymousType, []);
    }

    public static bool IsAnonymousTypeUsed(this CppType type, CppType anonymousType, HashSet<CppType> visited)
    {
        if (!visited.Add(type)) return false;

        if (ReferenceEquals(type, anonymousType)) return true;

        if (type is CppTypeWithElementType typeWithElementType)
        {
            return IsAnonymousTypeUsed(typeWithElementType.ElementType, anonymousType);
        }

        return false;
    }

    public static CppLinkageKind ToLinkageKind(this CXLinkageKind link)
    {
        return link switch
        {
            CXLinkageKind.CXLinkage_Invalid => CppLinkageKind.Invalid,
            CXLinkageKind.CXLinkage_NoLinkage => CppLinkageKind.NoLinkage,
            CXLinkageKind.CXLinkage_Internal => CppLinkageKind.Internal,
            CXLinkageKind.CXLinkage_UniqueExternal => CppLinkageKind.UniqueExternal,
            CXLinkageKind.CXLinkage_External => CppLinkageKind.External,
            _ => CppLinkageKind.Invalid,
        };
    }

    public static CppLinkageKind GetLinkageKind(this CXCursor cursor)
    {
        return cursor.Linkage.ToLinkageKind();
    }

    public static CppCallingConvention GetCallingConvention(this CXType type)
    {
        var callingConv = type.FunctionTypeCallingConv;
        return callingConv switch
        {
            CXCallingConv.CXCallingConv_Default => CppCallingConvention.Default,
            CXCallingConv.CXCallingConv_C => CppCallingConvention.C,
            CXCallingConv.CXCallingConv_X86StdCall => CppCallingConvention.X86StdCall,
            CXCallingConv.CXCallingConv_X86FastCall => CppCallingConvention.X86FastCall,
            CXCallingConv.CXCallingConv_X86ThisCall => CppCallingConvention.X86ThisCall,
            CXCallingConv.CXCallingConv_X86Pascal => CppCallingConvention.X86Pascal,
            CXCallingConv.CXCallingConv_AAPCS => CppCallingConvention.AAPCS,
            CXCallingConv.CXCallingConv_AAPCS_VFP => CppCallingConvention.AAPCS_VFP,
            CXCallingConv.CXCallingConv_X86RegCall => CppCallingConvention.X86RegCall,
            CXCallingConv.CXCallingConv_IntelOclBicc => CppCallingConvention.IntelOclBicc,
            CXCallingConv.CXCallingConv_Win64 => CppCallingConvention.Win64,
            CXCallingConv.CXCallingConv_X86_64SysV => CppCallingConvention.X86_64SysV,
            CXCallingConv.CXCallingConv_X86VectorCall => CppCallingConvention.X86VectorCall,
            CXCallingConv.CXCallingConv_Swift => CppCallingConvention.Swift,
            CXCallingConv.CXCallingConv_PreserveMost => CppCallingConvention.PreserveMost,
            CXCallingConv.CXCallingConv_PreserveAll => CppCallingConvention.PreserveAll,
            CXCallingConv.CXCallingConv_AArch64VectorCall => CppCallingConvention.AArch64VectorCall,
            CXCallingConv.CXCallingConv_Invalid => CppCallingConvention.Invalid,
            CXCallingConv.CXCallingConv_Unexposed => CppCallingConvention.Unexposed,
            _ => CppCallingConvention.Unexposed,
        };
    }

    public static CppSourceLocation ToSourceLocation(this in CXSourceLocation start)
    {
        start.GetFileLocation(out var file, out var line, out var column, out var offset);
        var fileNameStr = CXUtil.GetFileName(file);
        if (!string.IsNullOrEmpty(fileNameStr))
        {
            fileNameStr = Path.GetFullPath(fileNameStr);
        }
        return new CppSourceLocation(fileNameStr, (int)offset, (int)line, (int)column);
    }

    public static CppSourceLocation GetSourceLocation(this in CXCursor cursor)
    {
        return cursor.Location.ToSourceLocation();
    }

    public static CppSourceSpan ToSourceRange(this in CXSourceRange range)
    {
        var start = range.Start.ToSourceLocation();
        var end = range.End.ToSourceLocation();
        return new CppSourceSpan(start, end);
    }

    public static CppSourceSpan GetSourceRange(this in CXCursor cursor)
    {
        return cursor.Extent.ToSourceRange();
    }

    public static CppSourceLocation GetSourceLocation(this in CXDiagnostic diagnostic)
    {
        return diagnostic.Location.ToSourceLocation();
    }

    public static string GetCursorAsTextBetweenOffset(this in CXCursor cursor, int startOffset, int endOffset)
    {
        Tokenizer tokenizer = new(cursor);
        StringBuilder builder = new();
        var previousTokenKind = CppTokenKind.Punctuation;
        for (int i = 0; i < tokenizer.Count; i++)
        {
            var token = tokenizer[i];
            if (previousTokenKind.IsIdentifierOrKeyword() && token.Kind.IsIdentifierOrKeyword())
            {
                builder.Append(' ');
            }

            if (token.Span.Start.Offset >= startOffset && token.Span.End.Offset <= endOffset)
            {
                builder.Append(token.Text);
            }
        }
        return builder.ToString();
    }

    public static bool IsExpression(this in CXCursor cursor)
    {
        return cursor.Kind >= CXCursorKind.CXCursor_FirstExpr && cursor.Kind <= CXCursorKind.CXCursor_LastExpr;
    }

    public static string AsText(this in CXCursor cursor) => new Tokenizer(cursor).TokensToString();

    public static bool IsCursorDefinition(this in CXCursor cursor, CppElement element)
    {
        return cursor.IsDefinition || element is CppInclusionDirective || element is CppClass cppClass && (cppClass.ClassKind == CppClassKind.ObjCInterface ||
                                                                                                             cppClass.ClassKind == CppClassKind.ObjCProtocol ||
                                                                                                             cppClass.ClassKind == CppClassKind.ObjCInterfaceCategory)
            ;
    }
}
