using System;
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
/// Base interface of a <see cref = "ICppContainer"/> containing fields, functions, enums, classes, typedefs.
/// </summary>
/// <seealso cref = "CppClass"/>
public interface ICppDeclarationContainer : ICppContainer, ICppAttributeContainer
{
    /// <summary>
    /// Gets the fields/variables.
    /// </summary>
    CppContainerList<CppField> fields { get; }

    /// <summary>
    /// Gets the properties.
    /// </summary>
    CppContainerList<CppProperty> properties { get; }

    /// <summary>
    /// Gets the functions/methods.
    /// </summary>
    CppContainerList<CppFunction> functions { get; }

    /// <summary>
    /// Gets the enums.
    /// </summary>
    CppContainerList<CppEnum> enums { get; }

    /// <summary>
    /// Gets the classes, structs.
    /// </summary>
    CppContainerList<CppClass> classes { get; }

    /// <summary>
    /// Gets the typedefs.
    /// </summary>
    CppContainerList<CppTypedef> typedefs { get; }
    //Just use ICppAttributeContainer here(enum can support attribute, so we just use ICppAttributeContainer here)~~
    //CppContainerList<CppAttribute> Attributes { get; }
}

/// <summary>
/// Provides ordinal name lookup over current class, enum, function, and typedef collections.
/// </summary>
public static class ICppDeclarationContainerExtensions
{
    /// <summary>
    /// Finds the first matching native member in class, enum, function, then typedef category order.
    /// </summary>
    /// <typeparam name="T">
    /// The concrete declaration container type.
    /// </typeparam>
    /// <param name="container">
    /// The borrowed declaration container whose current collections are searched.
    /// </param>
    /// <param name="name">
    /// The case-sensitive unqualified native identifier.
    /// </param>
    /// <returns>
    /// The borrowed matching node, or null when none of the searched declaration categories contains that name.
    /// </returns>
    public static CppElement? FindByName<T>(
        this T container,
        ReadOnlySpan<char> name
    )
        where T : ICppDeclarationContainer
    {
        var c = container.classes.FindElementByName(name);
        if (c != null)
            return c;
        var e = container.enums.FindElementByName(name);
        if (e != null)
            return e;
        var f = container.functions.FindElementByName(name);
        if (f != null)
            return f;
        var t = container.typedefs.FindElementByName(name);
        if (t != null)
            return t;
        return null;
    }
}
