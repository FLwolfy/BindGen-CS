using System.Collections.Generic;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using BGCS.CppAst.Model.Interfaces;

namespace BGCS.CppAst.Utilities;

/// <summary>
/// Internal helper class for visiting children
/// </summary>
internal static class CppContainerHelper
{
    public static IEnumerable<ICppDeclaration> Children(ICppGlobalDeclarationContainer container)
    {
        foreach (var item in container.enums)
        {
            yield return item;
        }

        foreach (var item in container.classes)
        {
            yield return item;
        }

        foreach (var item in container.typedefs)
        {
            yield return item;
        }

        foreach (var item in container.fields)
        {
            yield return item;
        }

        foreach (var item in container.functions)
        {
            yield return item;
        }

        foreach (var item in container.namespaces)
        {
            yield return item;
        }
    }

    public static IEnumerable<ICppDeclaration> Children(ICppDeclarationContainer container)
    {
        foreach (var item in container.enums)
        {
            yield return item;
        }

        foreach (var item in container.classes)
        {
            yield return item;
        }

        foreach (var item in container.typedefs)
        {
            yield return item;
        }

        foreach (var item in container.fields)
        {
            yield return item;
        }

        foreach (var item in container.functions)
        {
            yield return item;
        }
    }
}
