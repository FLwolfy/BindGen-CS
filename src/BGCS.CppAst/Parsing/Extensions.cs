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

/// <summary>
/// Defines the public class <c>Extensions</c>.
/// </summary>
public static class Extensions
{
    /// <summary>
    /// Executes public operation <c>ToVisibility</c>.
    /// </summary>
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

    /// <summary>
    /// Returns computed data from <c>GetVisibility</c>.
    /// </summary>
    public static CppVisibility GetVisibility(this in CXCursor cursor)
    {
        return cursor.CXXAccessSpecifier.ToVisibility();
    }

    /// <summary>
    /// Returns computed data from <c>GetStorageQualifier</c>.
    /// </summary>
    public static CppStorageQualifier GetStorageQualifier(this in CXCursor cursor)
    {
        return cursor.StorageClass switch
        {
            CX_StorageClass.CX_SC_Extern or CX_StorageClass.CX_SC_PrivateExtern => CppStorageQualifier.Extern,
            CX_StorageClass.CX_SC_Static => CppStorageQualifier.Static,
            _ => CppStorageQualifier.None,
        };
    }

    /// <summary>
    /// Executes public operation <c>IsAnonymousTypeUsed</c>.
    /// </summary>
    public static bool IsAnonymousTypeUsed(this CppType type, CppType anonymousType)
    {
        return IsAnonymousTypeUsed(type, anonymousType, []);
    }

    /// <summary>
    /// Executes public operation <c>IsAnonymousTypeUsed</c>.
    /// </summary>
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

    /// <summary>
    /// Executes public operation <c>ToLinkageKind</c>.
    /// </summary>
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

    /// <summary>
    /// Returns computed data from <c>GetLinkageKind</c>.
    /// </summary>
    public static CppLinkageKind GetLinkageKind(this CXCursor cursor)
    {
        return cursor.Linkage.ToLinkageKind();
    }

    /// <summary>
    /// Executes public operation <c>IsExternC</c>.
    /// </summary>
    public static bool IsExternC(this in CXCursor cursor, in CXCursor parent)
    {
        if (parent.Kind == CXCursorKind.CXCursor_LinkageSpec && parent.IsExternCLinkageSpec())
        {
            return true;
        }

        if (cursor.LexicalParent.Kind == CXCursorKind.CXCursor_LinkageSpec && cursor.LexicalParent.IsExternCLinkageSpec())
        {
            return true;
        }

        if (cursor.SemanticParent.Kind == CXCursorKind.CXCursor_LinkageSpec && cursor.SemanticParent.IsExternCLinkageSpec())
        {
            return true;
        }

        // Fallback for single declaration style: extern "C" int func(...);
        if (cursor.StorageClass == CX_StorageClass.CX_SC_Extern)
        {
            var text = cursor.AsText();
            if (text.Contains("extern", StringComparison.Ordinal) && text.Contains("\"C\"", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Executes public operation <c>IsExternCLinkageSpec</c>.
    /// </summary>
    public static bool IsExternCLinkageSpec(this in CXCursor cursor)
    {
        if (cursor.Kind != CXCursorKind.CXCursor_LinkageSpec)
        {
            return false;
        }

        var text = cursor.AsText();
        return text.Contains("extern", StringComparison.Ordinal)
            && text.Contains("\"C\"", StringComparison.Ordinal)
            && !text.Contains("\"C++\"", StringComparison.Ordinal);
    }

    /// <summary>
    /// Returns computed data from <c>GetCallingConvention</c>.
    /// </summary>
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

    /// <summary>
    /// Executes public operation <c>ToSourceLocation</c>.
    /// </summary>
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

    /// <summary>
    /// Returns computed data from <c>GetSourceLocation</c>.
    /// </summary>
    public static CppSourceLocation GetSourceLocation(this in CXCursor cursor)
    {
        return cursor.Location.ToSourceLocation();
    }

    /// <summary>
    /// Executes public operation <c>ToSourceRange</c>.
    /// </summary>
    public static CppSourceSpan ToSourceRange(this in CXSourceRange range)
    {
        var start = range.Start.ToSourceLocation();
        var end = range.End.ToSourceLocation();
        return new CppSourceSpan(start, end);
    }

    /// <summary>
    /// Returns computed data from <c>GetSourceRange</c>.
    /// </summary>
    public static CppSourceSpan GetSourceRange(this in CXCursor cursor)
    {
        return cursor.Extent.ToSourceRange();
    }

    /// <summary>
    /// Returns computed data from <c>GetSourceLocation</c>.
    /// </summary>
    public static CppSourceLocation GetSourceLocation(this in CXDiagnostic diagnostic)
    {
        return diagnostic.Location.ToSourceLocation();
    }

    /// <summary>
    /// Returns computed data from <c>GetCursorAsTextBetweenOffset</c>.
    /// </summary>
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

    /// <summary>
    /// Executes public operation <c>IsExpression</c>.
    /// </summary>
    public static bool IsExpression(this in CXCursor cursor)
    {
        return cursor.Kind >= CXCursorKind.CXCursor_FirstExpr && cursor.Kind <= CXCursorKind.CXCursor_LastExpr;
    }

    /// <summary>
    /// Executes public operation <c>AsText</c>.
    /// </summary>
    public static string AsText(this in CXCursor cursor) => new Tokenizer(cursor).TokensToString();

    /// <summary>
    /// Executes public operation <c>IsCursorDefinition</c>.
    /// </summary>
    public static bool IsCursorDefinition(this in CXCursor cursor, CppElement element)
    {
        return cursor.IsDefinition || element is CppInclusionDirective || element is CppClass cppClass && (cppClass.ClassKind == CppClassKind.ObjCInterface ||
                                                                                                             cppClass.ClassKind == CppClassKind.ObjCProtocol ||
                                                                                                             cppClass.ClassKind == CppClassKind.ObjCInterfaceCategory)
            ;
    }
}
