// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BGCS.CppAst.AttributeParsing;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Attributes;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Parsing;
using BGCS.CppAst.Utilities;
using ClangSharp.Interop;

namespace BGCS.CppAst.Collections;

/// <summary>
/// A base Cpp container for macros, classes, fields, functions, enums, typesdefs.
/// </summary>
public class CppGlobalDeclarationContainer : CppElement, ICppGlobalDeclarationContainer
{
    private readonly Dictionary<ICppContainer, Dictionary<string, CacheByName>> m_multiCacheByName;
    /// <summary>
    /// Creates empty owned declaration collections and a lazy name-lookup cache for a borrowed native container.
    /// </summary>
    /// <param name="cursor">
    /// The borrowed Clang cursor, valid only while its owning compilation remains alive; default creates a synthetic node.
    /// </param>
    public CppGlobalDeclarationContainer(CXCursor cursor) : base(cursor)
    {
        this.m_multiCacheByName = new Dictionary<ICppContainer, Dictionary<string, CacheByName>>(ReferenceEqualityComparer<ICppContainer>.instance);
        this.macros = [];
        this.fields = new(this);
        this.functions = new(this);
        this.enums = new CppContainerList<CppEnum>(this);
        this.classes = new CppContainerList<CppClass>(this);
        this.typedefs = new CppContainerList<CppTypedef>(this);
        this.namespaces = new CppContainerList<CppNamespace>(this);
        this.attributes = [];
        this.properties = new CppContainerList<CppProperty>(this);
        this.inclusionDirectives = new CppContainerList<CppInclusionDirective>(this);
    }

    /// <summary>
    /// Gets the macros defines for this container.
    /// </summary>
    /// <remarks>
    /// Macros are only available if <see cref = "CppParserOptions.parseMacros"/> is <c>true</c>
    /// </remarks>
    public List<CppMacro> macros { get; }
    /// <inheritdoc/>
    public CppContainerList<CppField> fields { get; }
    /// <inheritdoc/>
    public CppContainerList<CppProperty> properties { get; }
    /// <inheritdoc/>
    public CppContainerList<CppFunction> functions { get; }
    /// <inheritdoc/>
    public CppContainerList<CppEnum> enums { get; }
    /// <inheritdoc/>
    public CppContainerList<CppClass> classes { get; }
    /// <inheritdoc/>
    public CppContainerList<CppTypedef> typedefs { get; }
    /// <inheritdoc/>
    public CppContainerList<CppNamespace> namespaces { get; }
    /// <inheritdoc/>
    public List<CppAttribute> attributes { get; }
    /// <summary>
    /// Gets mutable attributes recovered from source tokens when Clang does not expose an equivalent attribute node.
    /// </summary>
    public List<CppAttribute> tokenAttributes { get; } = [];
    /// <summary>
    /// Gets the mutable recognized annotation map owned by this container.
    /// </summary>
    public MetaAttributeMap metaAttributes { get; private set; } = new MetaAttributeMap();
    /// <summary>
    /// Gets the list of inclusion directives for this container.
    /// </summary>
    public CppContainerList<CppInclusionDirective> inclusionDirectives { get; }

    /// <summary>
    /// Provides the C++ scope separator used when resolving qualified declaration names.
    /// </summary>
    public static readonly string namespaceSeparator = "::";
    /// <inheritdoc/>
    public virtual IEnumerable<ICppDeclaration> children => CppContainerHelper.Children(this);

    /// <summary>
    /// Find a <see cref = "CppElement"/> by name declared directly by this container.
    /// </summary>
    /// <param name = "name">Name of the element to find</param>
    /// <returns>The CppElement found or null if not found</returns>
    public CppElement? FindByName(ReadOnlySpan<char> name)
    {
        return FindByName(this, name);
    }

    private static CppElement? SearchForChild(
        CppElement parent,
        ReadOnlySpan<char> childName
    ) {
        if (parent is CppNamespace ns)
        {
            var n = ns.namespaces.FindElementByName(childName);
            if (n != null)
            {
                return n;
            }

            var c = ns.FindByName(childName);
            if (c != null)
            {
                return c;
            }

            foreach (var sn in ns.namespaces)
            {
                if (sn.isInlineNamespace)
                {
                    var findElem = SearchForChild(sn, childName);
                    if (findElem != null)
                        return findElem;
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
    /// Find a <see cref = "CppElement"/> by a fully qualified name (for example, <c>example::Vector</c>).
    /// </summary>
    /// <param name = "name">Name of the element to find</param>
    /// <returns>The CppElement found or null if not found</returns>
    public CppElement? FindByFullName(ReadOnlySpan<char> name)
    {
        if (name.IsEmpty || name.IsWhiteSpace())
            return null;
        CppElement? elem = null;
        while (!name.IsEmpty)
        {
            var idx = name.IndexOf(global::BGCS.CppAst.Collections.CppGlobalDeclarationContainer.namespaceSeparator);
            if (idx == -1)
                idx = name.Length;
            var part = name[..idx];
            elem = elem == null ? FindByName(part) : SearchForChild(elem, part);
            if (elem == null)
                return null;
            if (idx == name.Length)
                break;
            name = name[(idx + global::BGCS.CppAst.Collections.CppGlobalDeclarationContainer.namespaceSeparator.Length)..];
        }

        return elem;
    }

    /// <summary>
    /// Finds a declaration of the requested AST type by its qualified name.
    /// </summary>
    /// <typeparam name="TCppElement">
    /// The required AST node type.
    /// </typeparam>
    /// <param name="name">
    /// The case-sensitive namespace/class-qualified declaration name.
    /// </param>
    /// <returns>
    /// The borrowed matching declaration, or null when the name is absent or has a different AST type.
    /// </returns>
    public TCppElement? FindByFullName<TCppElement>(ReadOnlySpan<char> name)
        where TCppElement : CppElement
    {
        return FindByFullName(name) as TCppElement;
    }

    /// <summary>
    /// Find a <see cref = "CppElement"/> by name declared within the specified container.
    /// </summary>
    /// <param name = "container">The container to search for the element by name</param>
    /// <param name = "name">Name of the element to find</param>
    /// <returns>The CppElement found or null if not found</returns>
    /// <exception cref = "ArgumentNullException"></exception>
    public CppElement? FindByName(
        ICppContainer container,
        ReadOnlySpan<char> name
    ) {
        var cacheByName = FindByNameInternal(container, name);
        return cacheByName.element;
    }

    /// <summary>
    /// Find a list of <see cref = "CppElement"/> matching name (overloads) declared within the specified container.
    /// </summary>
    /// <param name = "container">The container to search for the element by name</param>
    /// <param name = "name">Name of the element to find</param>
    /// <returns>A list of CppElement found or empty enumeration if not found</returns>
    /// <exception cref = "ArgumentNullException"></exception>
    public IEnumerable<CppElement> FindListByName(
        ICppContainer container,
        ReadOnlySpan<char> name
    ) {
        var cacheByName = FindByNameInternal(container, name);
        return cacheByName;
    }

    /// <summary>
    /// Finds the first directly declared overload having both the requested name and AST type.
    /// </summary>
    /// <typeparam name="TCppElement">
    /// The required AST node type.
    /// </typeparam>
    /// <param name="name">
    /// The case-sensitive unqualified native member name.
    /// </param>
    /// <returns>
    /// The borrowed matching declaration, or null when no overload has the requested AST type.
    /// </returns>
    public TCppElement? FindByName<TCppElement>(ReadOnlySpan<char> name)
        where TCppElement : CppElement
    {
        return FindByName<TCppElement>(this, name);
    }

    /// <summary>
    /// Finds the first matching overload in a borrowed container using this owner's name cache.
    /// </summary>
    /// <typeparam name="TCppElement">
    /// The required AST node type.
    /// </typeparam>
    /// <param name="container">
    /// The non-null container whose direct declarations are searched.
    /// </param>
    /// <param name="name">
    /// The case-sensitive unqualified native member name.
    /// </param>
    /// <returns>
    /// The borrowed matching declaration, or null when the name or requested AST type is absent.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// The searched container is null.
    /// </exception>
    public TCppElement? FindByName<TCppElement>(
        ICppContainer container,
        ReadOnlySpan<char> name
    )
        where TCppElement : CppElement
    {
        foreach (CppElement element in FindListByName(container, name))
        {
            if (element is TCppElement typedElement)
                return typedElement;
        }
        return null;
    }

    /// <summary>
    /// Clear the cache used by all <see cref="FindByName(ICppContainer, ReadOnlySpan{char})"/> functions.
    /// </summary>
    /// <remarks>
    /// Call after adding, removing, renaming, or reparenting declarations following an earlier name lookup.
    /// </remarks>
    public void ClearCacheByName()
    {
        this.m_multiCacheByName.Clear();
    }

    private CacheByName FindByNameInternal(
        ICppContainer container,
        ReadOnlySpan<char> name
    ) {
        ArgumentNullException.ThrowIfNull(container);
        if (!this.m_multiCacheByName.TryGetValue(container, out var cacheByNames))
        {
            cacheByNames = [];
            this.m_multiCacheByName.Add(container, cacheByNames);
            foreach (var element in container.children)
            {
                var cppElement = (CppElement)element;
                if (element is ICppMember member && !string.IsNullOrEmpty(member.name))
                {
                    var elementName = member.name;
                    if (!cacheByNames.TryGetValue(elementName, out var cacheByName))
                    {
                        cacheByName = new CacheByName();
                    }

                    if (cacheByName.element == null)
                    {
                        cacheByName.element = cppElement;
                    }
                    else
                    {
                        cacheByName.list ??= [];
                        cacheByName.list.Add(cppElement);
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
        public CppElement element;
        /// <summary>
        /// Exposes public member <c>List</c>.
        /// </summary>
        public List<CppElement> list;
        /// <summary>
        /// Returns computed data from <c>GetEnumerator</c>.
        /// </summary>
        public readonly IEnumerator<CppElement> GetEnumerator()
        {
            if (this.element != null)
                yield return this.element;
            if (this.list != null)
            {
                foreach (var cppElement in this.list)
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
    public static readonly ReferenceEqualityComparer<T> instance = new();
    private ReferenceEqualityComparer()
    {
    }

    /// <inheritdoc/>
    public bool Equals(
        T? x,
        T? y
    ) {
        return ReferenceEquals(x, y);
    }

    /// <inheritdoc/>
    public int GetHashCode(T obj)
    {
        return RuntimeHelpers.GetHashCode(obj);
    }
}
