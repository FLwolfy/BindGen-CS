using System;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

namespace BGCS.CppAst.Extensions;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Attributes;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Expressions;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Model.Types;

/// <summary>
/// Extension methods.
/// </summary>
public static class CppExtensions
{
    /// <summary>
    /// Gets a boolean indicating whether this token kind is an identifier or keyword
    /// </summary>
    /// <param name="kind">The token kind</param>
    /// <returns><c>true</c> if the token is an identifier or keyword, <c>false</c> otherwise</returns>
    public static bool IsIdentifierOrKeyword(this CppTokenKind kind)
    {
        return kind == CppTokenKind.Identifier || kind == CppTokenKind.Keyword;
    }

    /// <summary>
    /// Gets the display name of the specified type. If the type is <see cref="ICppMember"/> it will
    /// only use the name provided by <see cref="ICppMember.Name"/>
    /// </summary>
    /// <param name="type">The type</param>
    /// <returns>The display name</returns>
    public static string GetDisplayName(this CppType type)
    {
        if (type is ICppMember member) return member.Name;
        return type.ToString();
    }

    /// <summary>
    /// Gets a boolean indicating whether the attribute is a dllexport or visibility("default")
    /// </summary>
    /// <param name="attribute">The attribute to check against</param>
    /// <returns><c>true</c> if the attribute is a dllexport or visibility("default")</returns>
    public static bool IsPublicExport(this CppAttribute attribute)
    {
        return attribute.Name == "dllexport" || attribute.Name == "visibility" && attribute.Arguments == "\"default\"";
    }

    /// <summary>
    /// Gets a boolean indicating whether the function is a dllexport or visibility("default")
    /// </summary>
    /// <param name="cppClass">The class to check against</param>
    /// <returns><c>true</c> if the class is a dllexport or visibility("default")</returns>
    public static bool IsPublicExport(this CppClass cppClass)
    {
        if (cppClass.Attributes != null)
        {
            foreach (var attr in cppClass.Attributes)
            {
                if (attr.IsPublicExport()) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Gets a boolean indicating whether the function is a dllexport or visibility("default")
    /// </summary>
    /// <param name="function">The function to check against</param>
    /// <returns><c>true</c> if the function is a dllexport or visibility("default")</returns>
    public static bool IsPublicExport(this CppFunction function)
    {
        if (function.IsExternC)
        {
            return true;
        }

        if (function.Attributes != null)
        {
            foreach (var attr in function.Attributes)
            {
                if (attr.IsPublicExport()) return true;
            }
        }

        if (function.LinkageKind == CppLinkageKind.External || function.LinkageKind == CppLinkageKind.UniqueExternal)
        {
            return true;
        }

        // Fallback: some parsers may not classify `extern "C"` with External/UniqueExternal linkage.
        // For top-level non-static functions, treat them as publicly bindable.
        return !function.IsCxxClassMethod
            && function.StorageQualifier != CppStorageQualifier.Static;
    }
}
