// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.


using ClangSharp.Interop;
using BGCS.CppAst.AttributeUtils;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Attributes;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Parsing;
using BGCS.CppAst.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace BGCS.CppAst.Collections;
/// <summary>
/// A base Cpp container for macros, classes, fields, functions, enums, typesdefs.
/// </summary>
public class CppGlobalDeclarationContainer : CppElement, ICppGlobalDeclarationContainer
{
    private readonly Dictionary<ICppContainer, Dictionary<string, CacheByName>> multiCacheByName;

    /// <summary>
    /// Create a new instance of this container.
    /// </summary>
    public CppGlobalDeclarationContainer(CXCursor cursor) : base(cursor)
    {
        multiCacheByName = new Dictionary<ICppContainer, Dictionary<string, CacheByName>>(ReferenceEqualityComparer<ICppContainer>.Instance);
        Macros = [];
        Fields = new(this);
        Functions = new(this);
        Enums = new CppContainerList<CppEnum>(this);
        Classes = new CppContainerList<CppClass>(this);
        Typedefs = new CppContainerList<CppTypedef>(this);
        Namespaces = new CppContainerList<CppNamespace>(this);
        Attributes = [];
        TokenAttributes = [];
        Properties = new CppContainerList<CppProperty>(this);
        InclusionDirectives = new CppContainerList<CppInclusionDirective>(this);
    }

    /// <summary>
    /// Gets the macros defines for this container.
    /// </summary>
    /// <remarks>
    /// Macros are only available if <see cref="CppParserOptions.ParseMacros"/> is <c>true</c>
    /// </remarks>
    public List<CppMacro> Macros { get; }

    /// <inheritdoc />
    public CppContainerList<CppField> Fields { get; }

    /// <inheritdoc />
    public CppContainerList<CppProperty> Properties { get; }

    /// <inheritdoc />
    public CppContainerList<CppFunction> Functions { get; }

    /// <inheritdoc />
    public CppContainerList<CppEnum> Enums { get; }

    /// <inheritdoc />
    public CppContainerList<CppClass> Classes { get; }

    /// <inheritdoc />
    public CppContainerList<CppTypedef> Typedefs { get; }

    /// <inheritdoc />
    public CppContainerList<CppNamespace> Namespaces { get; }

    /// <inheritdoc />
    public List<CppAttribute> Attributes { get; }

    [Obsolete("TokenAttributes is deprecated. please use system attributes and annotate attributes")]
    /// <summary>
    /// Gets <c>TokenAttributes</c>.
    /// </summary>
    public List<CppAttribute> TokenAttributes { get; }

    /// <summary>
    /// Gets or sets <c>MetaAttributes</c>.
    /// </summary>
    public MetaAttributeMap MetaAttributes { get; private set; } = new MetaAttributeMap();

    /// <summary>
    /// Gets the list of inclusion directives for this container.
    /// </summary>
    public CppContainerList<CppInclusionDirective> InclusionDirectives { get; }

    /// <summary>
    /// Exposes public member <c>"::"</c>.
    /// </summary>
    public static readonly string NamespaceSeparator = "::";

    /// <inheritdoc />
    public virtual IEnumerable<ICppDeclaration> Children => CppContainerHelper.Children(this);

    /// <summary>
    /// Find a <see cref="CppElement"/> by name declared directly by this container.
    /// </summary>
    /// <param name="name">Name of the element to find</param>
    /// <returns>The CppElement found or null if not found</returns>
    public CppElement FindByName(ReadOnlySpan<char> name)
    {
        return FindByName(this, name);
    }

    private static CppElement? SearchForChild(CppElement parent, ReadOnlySpan<char> childName)
    {
        if (parent is CppNamespace ns)
        {
            var n = ns.Namespaces.FindElementByName(childName);
            if (n != null)
            {
                return n;
            }

            var c = ns.FindByName(childName);
            if (c != null)
            {
                return c;
            }

            foreach (var sn in ns.Namespaces)
            {
                if (sn.IsInlineNamespace)
                {
                    var findElem = SearchForChild(sn, childName);
                    if (findElem != null) return findElem;
                }
            }
        }
        else if (parent is ICppDeclarationContainer declarationContainer)
        {
            return declarationContainer.FindByName(childName);
        }

        return null;
    }

    /// <summary>
    /// Find a <see cref="CppElement"/> by full name(such as gbf::math::Vector3).
    /// </summary>
    /// <param name="name">Name of the element to find</param>
    /// <returns>The CppElement found or null if not found</returns>
	public CppElement? FindByFullName(ReadOnlySpan<char> name)
    {
        if (name.IsEmpty || name.IsWhiteSpace()) return null;

        CppElement? elem = null;
        while (!name.IsEmpty)
        {
            var idx = name.IndexOf(NamespaceSeparator);
            if (idx == -1) idx = name.Length;
            var part = name[..idx];

            elem = elem == null ? FindByName(part) : SearchForChild(elem, part);

            if (elem == null) return null;
            if (idx == name.Length) break;
            name = name[(idx + NamespaceSeparator.Length)..];
        }
        return elem;
    }

    /// <summary>
    /// Find a <see cref="CppElement"/> by full name(such as gbf::math::Vector3).
    /// </summary>
    /// <param name="name">Name of the element to find</param>
    /// <returns>The CppElement found or null if not found</returns>
    public TCppElement? FindByFullName<TCppElement>(ReadOnlySpan<char> name) where TCppElement : CppElement
    {
        return (TCppElement?)FindByFullName(name);
    }

    /// <summary>
    /// Find a <see cref="CppElement"/> by name declared within the specified container.
    /// </summary>
    /// <param name="container">The container to search for the element by name</param>
    /// <param name="name">Name of the element to find</param>
    /// <returns>The CppElement found or null if not found</returns>
    /// <exception cref="ArgumentNullException"></exception>
    public CppElement FindByName(ICppContainer container, ReadOnlySpan<char> name)
    {
        var cacheByName = FindByNameInternal(container, name);
        return cacheByName.Element;
    }

    /// <summary>
    /// Find a list of <see cref="CppElement"/> matching name (overloads) declared within the specified container.
    /// </summary>
    /// <param name="container">The container to search for the element by name</param>
    /// <param name="name">Name of the element to find</param>
    /// <returns>A list of CppElement found or empty enumeration if not found</returns>
    /// <exception cref="ArgumentNullException"></exception>
    public IEnumerable<CppElement> FindListByName(ICppContainer container, ReadOnlySpan<char> name)
    {
        var cacheByName = FindByNameInternal(container, name);
        return cacheByName;
    }

    /// <summary>
    /// Find a <see cref="CppElement"/> by name and type declared directly by this container.
    /// </summary>
    /// <param name="name">Name of the element to find</param>
    /// <returns>The CppElement found or null if not found</returns>
    public TCppElement? FindByName<TCppElement>(ReadOnlySpan<char> name) where TCppElement : CppElement
    {
        return FindByName<TCppElement>(this, name);
    }

    /// <summary>
    /// Find a <see cref="CppElement"/> by name and type declared within the specified container.
    /// </summary>
    /// <param name="container">The container to search for the element by name</param>
    /// <param name="name">Name of the element to find</param>
    /// <returns>The CppElement found or null if not found</returns>
    /// <exception cref="ArgumentNullException"></exception>
    public TCppElement? FindByName<TCppElement>(ICppContainer container, ReadOnlySpan<char> name) where TCppElement : CppElement
    {
        return (TCppElement?)FindByName(container, name);
    }

    /// <summary>
    /// Clear the cache used by all <see cref="FindByName(string)"/> functions.
    /// </summary>
    /// <remarks>
    /// Used this method when new elements are added to this instance.
    /// </remarks>
    public void ClearCacheByName()
    {
        // TODO: reuse previous internal cache
        multiCacheByName.Clear();
    }

    private CacheByName FindByNameInternal(ICppContainer container, ReadOnlySpan<char> name)
    {
        if (!multiCacheByName.TryGetValue(container, out var cacheByNames))
        {
            cacheByNames = [];
            multiCacheByName.Add(container, cacheByNames);

            foreach (var element in container.Children)
            {
                var cppElement = (CppElement)element;
                if (element is ICppMember member && !string.IsNullOrEmpty(member.Name))
                {
                    var elementName = member.Name;
                    if (!cacheByNames.TryGetValue(elementName, out var cacheByName))
                    {
                        cacheByName = new CacheByName();
                    }

                    if (cacheByName.Element == null)
                    {
                        cacheByName.Element = cppElement;
                    }
                    else
                    {
                        cacheByName.List ??= [];
                        cacheByName.List.Add(cppElement);
                    }

                    cacheByNames[elementName] = cacheByName;
                }
            }
        }

        var lookup = cacheByNames.GetAlternateLookup<ReadOnlySpan<char>>();
        return lookup.TryGetValue(name, out var cacheByNameFound) ? cacheByNameFound : new CacheByName();
    }

    private struct CacheByName : IEnumerable<CppElement>
    {
        /// <summary>
        /// Exposes public member <c>Element</c>.
        /// </summary>
        public CppElement Element;
        /// <summary>
        /// Exposes public member <c>List</c>.
        /// </summary>
        public List<CppElement> List;

        /// <summary>
        /// Returns computed data from <c>GetEnumerator</c>.
        /// </summary>
        public readonly IEnumerator<CppElement> GetEnumerator()
        {
            if (Element != null) yield return Element;
            if (List != null)
            {
                foreach (var cppElement in List)
                {
                    yield return cppElement;
                }
            }
        }

        readonly IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}

internal class ReferenceEqualityComparer<T> : IEqualityComparer<T>
{
    public static readonly ReferenceEqualityComparer<T> Instance = new();

    private ReferenceEqualityComparer()
    {
    }

    /// <inheritdoc />
    public bool Equals(T? x, T? y)
    {
        return ReferenceEquals(x, y);
    }

    /// <inheritdoc />
    public int GetHashCode(T obj)
    {
        return RuntimeHelpers.GetHashCode(obj);
    }
}
