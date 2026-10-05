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
    /// <param name = "kind">The token kind</param>
    /// <returns><c>true</c> if the token is an identifier or keyword, <c>false</c> otherwise</returns>
    public static bool IsIdentifierOrKeyword(this CppTokenKind kind)
    {
        return kind == CppTokenKind.Identifier || kind == CppTokenKind.Keyword;
    }

    /// <summary>
    /// Gets the display name of the specified type. If the type is <see cref = "ICppMember"/> it will
    /// only use the name provided by <see cref = "ICppMember.name"/>
    /// </summary>
    /// <param name = "type">The type</param>
    /// <returns>The display name</returns>
    public static string GetDisplayName(this CppType type)
    {
        if (type is ICppMember member)
            return member.name;
        return type.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Gets a boolean indicating whether the attribute is a dllexport or visibility("default")
    /// </summary>
    /// <param name = "attribute">The attribute to check against</param>
    /// <returns><c>true</c> if the attribute is a dllexport or visibility("default")</returns>
    public static bool IsPublicExport(this CppAttribute attribute)
    {
        return attribute.name == "dllexport" || attribute.name == "visibility" && attribute.arguments == "\"default\"";
    }

    /// <summary>
    /// Checks whether a class declaration carries an explicit public export attribute.
    /// </summary>
    /// <param name = "cppClass">The class to check against</param>
    /// <returns><c>true</c> if the class is a dllexport or visibility("default")</returns>
    public static bool IsPublicExport(this CppClass cppClass)
    {
        if (cppClass.attributes != null)
        {
            foreach (var attr in cppClass.attributes)
            {
                if (attr.IsPublicExport())
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks whether a non-static free function has externally bindable linkage or an export declaration.
    /// </summary>
    /// <param name = "function">The function to check against</param>
    /// <returns>
    /// True for external declarations; false for class methods and static functions.
    /// This checks source linkage, not the export table of a compiled library.
    /// </returns>
    public static bool IsPublicExport(this CppFunction function)
    {
        if (function.isCxxClassMethod || (function.storageQualifier & CppStorageQualifier.Static) != 0)
            return false;
        if (function.isExternC)
        {
            return true;
        }

        if (function.attributes != null)
        {
            foreach (var attr in function.attributes)
            {
                if (attr.IsPublicExport())
                    return true;
            }
        }

        if (function.linkageKind == CppLinkageKind.External || function.linkageKind == CppLinkageKind.UniqueExternal)
        {
            return true;
        }

        return false;
    }
}
