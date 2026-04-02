using System.IO;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

using ClangSharp.Interop;
using BGCS.CppAst.AttributeUtils;
using BGCS.CppAst.Model.Attributes;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Model.Metadata;
using BGCS.CppAst.Parsing;
using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace BGCS.CppAst.Model;
/// <summary>
/// Base class for all Cpp elements of the AST nodes.
/// </summary>
public abstract class CppElement : ICppElement
{
    private string? cachedFullParentName;

    protected CppElement(CXCursor cursor)
    {
        Cursor = cursor;
    }

    public CXCursor Cursor { get; set; }

    /// <summary>
    /// Gets or sets the source span of this element.
    /// </summary>
    public CppSourceSpan Span;

    /// <summary>
    /// Gets or sets the parent container of this element. Might be null.
    /// </summary>
    public ICppContainer? Parent { get; internal set; }

    public override sealed bool Equals(object? obj) => ReferenceEquals(this, obj);

    public override sealed int GetHashCode() => RuntimeHelpers.GetHashCode(this);

    public string FullParentName
    {
        get
        {
            if (cachedFullParentName is not null)
            {
                return cachedFullParentName;
            }

            StringBuilder sb = new();
            var p = Parent;
            while (p != null)
            {
                if (p is CppClass cpp)
                {
                    sb.Insert(0, $"{cpp.Name}::");
                    p = cpp.Parent;
                }
                else if (p is CppNamespace ns)
                {
                    // Just ignore inline namespace
                    if (!ns.IsInlineNamespace)
                    {
                        sb.Insert(0, $"{ns.Name}::");
                    }
                    p = ns.Parent;
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

            cachedFullParentName = sb.ToString();
            return cachedFullParentName;
        }
    }

    /// <summary>
    /// Gets the source file of this element.
    /// </summary>
    public string SourceFile => Span.Start.File;

    public void AssignSourceSpan(in CXCursor cursor)
    {
        var start = cursor.Extent.Start;
        var end = cursor.Extent.End;
        if (Span.Start.File is null)
        {
            Span = new CppSourceSpan(start.ToSourceLocation(), end.ToSourceLocation());
        }
    }

    [Obsolete("Remove me later, when all meta attributes are handled after the new api")]
    public void ConvertToMetaAttributes()
    {
        if (this is not ICppAttributeContainer container) return;
        foreach (var attr in container.Attributes)
        {
            //Now we only handle for annotate attribute here
            if (attr.Kind == AttributeKind.AnnotateAttribute)
            {
                MetaAttribute? metaAttr = null;

                metaAttr = CustomAttributeTool.ParseMetaStringFor(attr.Arguments, out string? errorMessage);

                if (!string.IsNullOrEmpty(errorMessage))
                {
                    throw new Exception($"handle meta not right, detail: `{errorMessage}, location: `{Span}`");
                }

                container.MetaAttributes.Append(metaAttr);
            }
        }
    }
}
