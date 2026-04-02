using System;
using System.Collections.Generic;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

using BGCS.CppAst.Collections;
using BGCS.CppAst.Model.Declarations;

namespace BGCS.CppAst.Model.Interfaces;
/// <summary>
/// Base interface of a <see cref="ICppContainer"/> containing fields, functions, enums, classes, typedefs.
/// </summary>
/// <seealso cref="CppClass"/>
public interface ICppDeclarationContainer : ICppContainer, ICppAttributeContainer
{
    /// <summary>
    /// Gets the fields/variables.
    /// </summary>
    CppContainerList<CppField> Fields { get; }

    /// <summary>
    /// Gets the properties.
    /// </summary>
    CppContainerList<CppProperty> Properties { get; }

    /// <summary>
    /// Gets the functions/methods.
    /// </summary>
    CppContainerList<CppFunction> Functions { get; }

    /// <summary>
    /// Gets the enums.
    /// </summary>
    CppContainerList<CppEnum> Enums { get; }

    /// <summary>
    /// Gets the classes, structs.
    /// </summary>
    CppContainerList<CppClass> Classes { get; }

    /// <summary>
    /// Gets the typedefs.
    /// </summary>
    CppContainerList<CppTypedef> Typedefs { get; }

    //Just use ICppAttributeContainer here(enum can support attribute, so we just use ICppAttributeContainer here)~~
    //CppContainerList<CppAttribute> Attributes { get; }
}

public static class ICppDeclarationContainerExtensions
{
    public static CppElement? FindByName<T>(this T container, ReadOnlySpan<char> name) where T : ICppDeclarationContainer
    {
        var c = container.Classes.FindElementByName(name);
        if (c != null) return c;

        var e = container.Enums.FindElementByName(name);
        if (e != null) return e;

        var f = container.Functions.FindElementByName(name);
        if (f != null) return f;

        var t = container.Typedefs.FindElementByName(name);
        if (t != null) return t;

        return null;
    }
}
