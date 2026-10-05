using System;
using System.Runtime.CompilerServices;
using System.Text;
using BGCS.CppAst.AttributeParsing;
using BGCS.CppAst.Model.Attributes;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Model.Metadata;
using BGCS.CppAst.Parsing;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using ClangSharp.Interop;

namespace BGCS.CppAst.Model;

/// <summary>
/// Base class for all Cpp elements of the AST nodes.
/// </summary>
public abstract class CppElement : ICppElement
{
    /// <summary>
    /// Creates an analysis model borrowing native data from its owning compilation.
    /// </summary>
    /// <param name="cursor">Borrowed native cursor, valid only while the owning compilation remains alive.</param>
    protected CppElement(CXCursor cursor)
    {
        this.cursor = cursor;
    }

    /// <summary>
    /// Gets or sets the borrowed Clang cursor; accessing native data requires the owning compilation to remain alive.
    /// </summary>
    public CXCursor cursor { get; set; }

    /// <summary>
    /// Gets or sets the source span of this element.
    /// </summary>
    public CppSourceSpan span;
    /// <summary>
    /// Gets or sets the parent container of this element. Might be null.
    /// </summary>
    public ICppContainer? parent { get; internal set; }

    /// <summary>
    /// Compares AST nodes by managed reference identity rather than native declaration spelling.
    /// </summary>
    /// <param name="obj">
    /// The object to compare with this node.
    /// </param>
    /// <returns>
    /// True only when the supplied object is this same managed node.
    /// </returns>
    public override sealed bool Equals(object? obj) => ReferenceEquals(this, obj);
    /// <summary>
    /// Hashes stable managed reference identity independently of mutable node fields.
    /// </summary>
    /// <returns>
    /// The process-local object identity hash, unsuitable for persisted binding identities.
    /// </returns>
    public override sealed int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    /// <summary>
    /// Gets the current enclosing class and non-inline namespace names, excluding this node and any trailing namespace separator.
    /// </summary>
    public string fullParentName
    {
        get
        {
            StringBuilder sb = new();
            var p = this.parent;
            while (p != null)
            {
                if (p is CppClass cpp)
                {
                    sb.Insert(0, $"{cpp.name}::");
                    p = cpp.parent;
                }
                else if (p is CppNamespace ns)
                {
                    // Just ignore inline namespace
                    if (!ns.isInlineNamespace)
                    {
                        sb.Insert(0, $"{ns.name}::");
                    }

                    p = ns.parent;
                }
                else
                {
                    // root namespace here, or no known parent, just ignore~
                    p = null;
                }
            }

            // Try to remove not need `::` in string tails.
            var len = sb.Length;
            if (len > 2 && sb[len - 1] == ':' && sb[len - 2] == ':')
            {
                sb.Length -= 2;
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Gets the source file of this element.
    /// </summary>
    public string sourceFile => this.span.start.file;

    /// <summary>
    /// Fills an unassigned source span from a borrowed cursor without replacing an existing span.
    /// </summary>
    /// <param name="cursor">
    /// The native cursor supplying file and extent coordinates while its compilation remains alive.
    /// </param>
    public void AssignSourceSpan(in CXCursor cursor)
    {
        var start = cursor.Extent.Start;
        var end = cursor.Extent.End;
        if (this.span.start.file is null)
        {
            this.span = new CppSourceSpan(start.ToSourceLocation(), end.ToSourceLocation());
        }
    }

    /// <summary>
    /// Merges recognized BGCS annotate attributes into this node's metadata map; other annotations and non-attribute nodes are ignored.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A recognized metadata annotation has malformed arguments; its source span identifies the failure.
    /// </exception>
    public void ConvertToMetaAttributes()
    {
        if (this is not ICppAttributeContainer container)
            return;
        foreach (var attr in container.attributes)
        {
            //Now we only handle for annotate attribute here
            if (attr.kind == AttributeKind.AnnotateAttribute)
            {
                MetaAttribute? metaAttr = null;
                metaAttr = CustomAttributeTool.ParseMetaStringFor(attr.arguments ?? string.Empty, out string? errorMessage);
                if (!string.IsNullOrEmpty(errorMessage))
                {
                    throw new InvalidOperationException($"Invalid metadata annotation at '{this.span}': {errorMessage}");
                }

                container.metaAttributes.Append(metaAttr);
            }
        }
    }
}
