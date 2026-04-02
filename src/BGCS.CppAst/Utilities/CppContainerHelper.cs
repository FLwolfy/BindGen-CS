using System;
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
        foreach (var item in container.Enums)
        {
            yield return item;
        }

        foreach (var item in container.Classes)
        {
            yield return item;
        }

        foreach (var item in container.Typedefs)
        {
            yield return item;
        }

        foreach (var item in container.Fields)
        {
            yield return item;
        }

        foreach (var item in container.Functions)
        {
            yield return item;
        }

        foreach (var item in container.Namespaces)
        {
            yield return item;
        }
    }

    public static IEnumerable<ICppDeclaration> Children(ICppDeclarationContainer container)
    {
        foreach (var item in container.Enums)
        {
            yield return item;
        }

        foreach (var item in container.Classes)
        {
            yield return item;
        }

        foreach (var item in container.Typedefs)
        {
            yield return item;
        }

        foreach (var item in container.Fields)
        {
            yield return item;
        }

        foreach (var item in container.Functions)
        {
            yield return item;
        }
    }
}
