using System;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

using ClangSharp;
using ClangSharp.Interop;
using BGCS.CppAst.Model.Attributes;
using BGCS.CppAst.Model.Interfaces;
using System.Collections.Generic;
using System.Text;

namespace BGCS.CppAst.Model.Metadata;
/// <summary>
/// Top level comment container.
/// </summary>
public class CppCommentFull : CppComment
{
    /// <summary>
    /// Initializes a new instance of <see cref="CppCommentFull"/>.
    /// </summary>
    public CppCommentFull(CXComment comment) : base(comment, CppCommentKind.Full)
    {
    }

    protected internal override void ToString(StringBuilder builder)
    {
        ChildrenToString(builder);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return base.ToString().TrimEnd();
    }
}

/// <summary>
/// Base class for all comments.
/// </summary>
public abstract class CppComment
{
    protected CppComment(CXComment comment, CppCommentKind kind)
    {
        Kind = kind;
    }

    /// <summary>
    /// Gets or sets <c>Comment</c>.
    /// </summary>
    public CXComment Comment { get; set; }

    /// <summary>
    /// The kind of comments.
    /// </summary>
    public CppCommentKind Kind { get; }

    /// <summary>
    /// Gets a list of children. Might be null.
    /// </summary>
    public List<CppComment> Children { get; set; }

    protected internal abstract void ToString(StringBuilder builder);

    protected void ChildrenToString(StringBuilder builder)
    {
        if (Children != null)
        {
            foreach (var children in Children)
            {
                children.ToString(builder);
            }
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        var builder = new StringBuilder();
        ToString(builder);
        return builder.ToString();
    }

    /// <summary>
    /// Executes public operation <c>ChildrenToString</c>.
    /// </summary>
    public string ChildrenToString()
    {
        var builder = new StringBuilder();
        ChildrenToString(builder);
        return builder.ToString();
    }

    /// <summary>
    /// Executes public operation <c>TryToParseAttributes</c>.
    /// </summary>
    public void TryToParseAttributes(ICppAttributeContainer attrContainer)
    {
        if (this is CppCommentText ctxt && ctxt.Text != null)
        {
            var txt = ctxt.Text.Trim();
            if (txt.StartsWith("[[") && txt.EndsWith("]]"))
            {
                attrContainer.Attributes.Add(new CppAttribute(Comment, "comment", AttributeKind.CommentAttribute)
                {
                    Arguments = txt,
                    Scope = "",
                    IsVariadic = false,
                });
            }
        }

        if (Children != null)
        {
            foreach (var child in Children)
            {
                child.TryToParseAttributes(attrContainer);
            }
        }
    }
}

/// <summary>
/// A comment that is a command (e.g `@param arg1`)
/// </summary>
public abstract class CppCommentCommand : CppComment
{
    protected CppCommentCommand(CXComment comment, CppCommentKind kind) : base(comment, kind)
    {
        Arguments = [];
    }

    /// <summary>
    /// Gets or sets <c>CommandName</c>.
    /// </summary>
    public string CommandName { get; set; }

    /// <summary>
    /// Gets <c>Arguments</c>.
    /// </summary>
    public List<string> Arguments { get; }

    protected internal override void ToString(StringBuilder builder)
    {
        builder.Append($"@{CommandName}");
        for (var index = 0; index < Arguments.Count; index++)
        {
            var argument = Arguments[index];
            builder.Append(' ');
            builder.Append(argument);
        }
        builder.Append(' ');
    }
}

/// <summary>
/// A comment paragraph.
/// </summary>
public class CppCommentParagraph : CppComment
{
    /// <summary>
    /// Initializes a new instance of <see cref="CppCommentParagraph"/>.
    /// </summary>
    public CppCommentParagraph(CXComment comment) : base(comment, CppCommentKind.Paragraph)
    {
    }

    protected internal override void ToString(StringBuilder builder)
    {
        if (Children != null)
        {
            for (var i = 0; i < Children.Count; i++)
            {
                var children = Children[i];
                children.ToString(builder);
                // If a text is followed by a text, we assume that it was a new line
                // between the two
                if (children.Kind == CppCommentKind.Text && i + 1 < Children.Count && Children[i + 1].Kind == CppCommentKind.Text)
                {
                    var text = ((CppCommentText)children).Text;
                    var nextText = ((CppCommentText)children).Text;
                    if (!string.IsNullOrEmpty(text) || !string.IsNullOrEmpty(nextText))
                    {
                        builder.AppendLine();
                    }
                }
            }
        }
        builder.AppendLine();
    }
}

/// <summary>
/// A comment block command (`@code ... @endcode`)
/// </summary>
public class CppCommentBlockCommand : CppCommentCommand
{
    /// <summary>
    /// Initializes a new instance of <see cref="CppCommentBlockCommand"/>.
    /// </summary>
    public CppCommentBlockCommand(CXComment comment) : base(comment, CppCommentKind.BlockCommand)
    {
    }

    protected internal override void ToString(StringBuilder builder)
    {
        base.ToString(builder);
        ChildrenToString(builder);
    }
}

/// <summary>
/// An inline comment command.
/// </summary>
public class CppCommentInlineCommand : CppCommentCommand
{
    /// <summary>
    /// Initializes a new instance of <see cref="CppCommentInlineCommand"/>.
    /// </summary>
    public CppCommentInlineCommand(CXComment comment) : base(comment, CppCommentKind.InlineCommand)
    {
    }

    /// <summary>
    /// Gets or sets <c>RenderKind</c>.
    /// </summary>
    public CppCommentInlineCommandRenderKind RenderKind { get; set; }

    protected internal override void ToString(StringBuilder builder)
    {
        base.ToString(builder);
        ChildrenToString(builder);
    }
}

/// <summary>
/// Type of rendering for an <see cref="CppCommentInlineCommand"/>
/// </summary>
public enum CppCommentInlineCommandRenderKind
{
    Normal,
    Bold,
    Monospaced,
    Emphasized,
}

/// <summary>
/// A comment for a function/method parameter.
/// </summary>
public class CppCommentParamCommand : CppCommentCommand
{
    /// <summary>
    /// Initializes a new instance of <see cref="CppCommentParamCommand"/>.
    /// </summary>
    public CppCommentParamCommand(CXComment comment) : base(comment, CppCommentKind.ParamCommand)
    {
    }

    /// <summary>
    /// Gets or sets the name of the parameter.
    /// </summary>
    public string ParamName { get; set; }

    /// <summary>
    /// Gets or sets a boolean indicating if the <see cref="ParamIndex"/> is valid.
    /// </summary>
    public bool IsParamIndexValid { get; set; }

    /// <summary>
    /// Gets or sets the index of this parameter in the function parameters.
    /// </summary>
    public int ParamIndex { get; set; }

    /// <summary>
    /// Gets or sets the direction of this parameter (in, out, inout).
    /// </summary>
    public CppCommentParamDirection Direction { get; set; }

    /// <summary>
    /// Gets or sets a boolean indicating if <see cref="Direction"/> was explicitly specified.
    /// </summary>
    public bool IsDirectionExplicit { get; set; }

    protected internal override void ToString(StringBuilder builder)
    {
        base.ToString(builder);
        builder.Append(ParamName);
        builder.Append(' ');
        ChildrenToString(builder);
    }
}

/// <summary>
/// A comment for a template parameter command.
/// </summary>
public class CppCommentTemplateParamCommand : CppCommentCommand
{
    /// <summary>
    /// Initializes a new instance of <see cref="CppCommentTemplateParamCommand"/>.
    /// </summary>
    public CppCommentTemplateParamCommand(CXComment comment) : base(comment, CppCommentKind.TemplateParamCommand)
    {
    }

    /// <summary>
    /// Gets or sets the name of the parameter.
    /// </summary>
    public string ParamName { get; set; }

    /// <summary>
    /// Depth or this parameter.
    /// </summary>
    public int Depth { get; set; }

    /// <summary>
    /// Gets or sets a boolean indicating if this <see cref="Index"/> is valid
    /// </summary>
    public bool IsPositionValid { get; set; }

    /// <summary>
    /// Gets or sets the index of this template parameter.
    /// </summary>
    public int Index { get; set; }

    protected internal override void ToString(StringBuilder builder)
    {
        base.ToString(builder);
        builder.Append(ParamName);
        builder.Append(' ');
        ChildrenToString(builder);
    }
}

/// <summary>
/// Direction used by <see cref="CppCommentParamCommand"/>
/// </summary>
public enum CppCommentParamDirection
{
    In,
    Out,
    InOut,
}

/// <summary>
/// An enumeration for <see cref="CppComment"/>
/// </summary>
public enum CppCommentKind
{
    Null = 0,
    Text = 1,
    InlineCommand = 2,
    HtmlStartTag = 3,
    HtmlEndTag = 4,
    Paragraph = 5,
    BlockCommand = 6,
    ParamCommand = 7,
    TemplateParamCommand = 8,
    VerbatimBlockCommand = 9,
    VerbatimBlockLine = 10,
    VerbatimLine = 11,
    Full = 12,
}

/// <summary>
/// A comment for a verbatim block command.
/// </summary>
public class CppCommentVerbatimBlockCommand : CppCommentCommand
{
    /// <summary>
    /// Initializes a new instance of <see cref="CppCommentVerbatimBlockCommand"/>.
    /// </summary>
    public CppCommentVerbatimBlockCommand(CXComment comment) : base(comment, CppCommentKind.VerbatimBlockCommand)
    {
    }

    protected internal override void ToString(StringBuilder builder)
    {
        base.ToString(builder);
        ChildrenToString(builder);
        builder.AppendLine($"@end{CommandName}");
    }
}

/// <summary>
/// A comment for a verbatim line inside a verbatim block.
/// </summary>
public class CppCommentVerbatimBlockLine : CppCommentTextBase
{
    /// <summary>
    /// Initializes a new instance of <see cref="CppCommentVerbatimBlockLine"/>.
    /// </summary>
    public CppCommentVerbatimBlockLine(CXComment comment) : base(comment, CppCommentKind.VerbatimBlockLine)
    {
    }

    protected internal override void ToString(StringBuilder builder)
    {
        base.ToString(builder);
        builder.AppendLine();
    }
}

/// <summary>
/// Base class for all text based comments.
/// </summary>
public abstract class CppCommentTextBase : CppComment
{
    protected CppCommentTextBase(CXComment comment, CppCommentKind kind) : base(comment, kind)
    {
    }

    /// <summary>
    /// Gets or sets <c>Text</c>.
    /// </summary>
    public string? Text { get; set; }

    protected internal override void ToString(StringBuilder builder)
    {
        builder.Append(Text);
    }
}

/// <summary>
/// A simple text comment entry.
/// </summary>
public class CppCommentText : CppCommentTextBase
{
    /// <summary>
    /// Initializes a new instance of <see cref="CppCommentText"/>.
    /// </summary>
    public CppCommentText(CXComment comment) : base(comment, CppCommentKind.Text)
    {
    }
}

/// <summary>
/// A verbatim line comment.
/// </summary>
public class CppCommentVerbatimLine : CppCommentTextBase
{
    /// <summary>
    /// Initializes a new instance of <see cref="CppCommentVerbatimLine"/>.
    /// </summary>
    public CppCommentVerbatimLine(CXComment comment) : base(comment, CppCommentKind.VerbatimLine)
    {
    }

    protected internal override void ToString(StringBuilder builder)
    {
        base.ToString(builder);
        builder.AppendLine();
    }
}

/// <summary>
/// Base class for an HTML comment start or en tag.
/// </summary>
public abstract class CppCommentHtmlTag : CppComment
{
    protected CppCommentHtmlTag(CXComment comment, CppCommentKind kind) : base(comment, kind)
    {
    }

    /// <summary>
    /// Gets or sets <c>TagName</c>.
    /// </summary>
    public string TagName { get; set; }

    protected internal abstract override void ToString(StringBuilder builder);
}

/// <summary>
/// An HTML start comment tag.
/// </summary>
public class CppCommentHtmlStartTag : CppCommentHtmlTag
{
    /// <summary>
    /// Initializes a new instance of <see cref="CppCommentHtmlStartTag"/>.
    /// </summary>
    public CppCommentHtmlStartTag(CXComment comment) : base(comment, CppCommentKind.HtmlStartTag)
    {
        Attributes = [];
    }

    /// <summary>
    /// Gets or sets a boolean indicating if this start tag is self closing.
    /// </summary>
    public bool IsSelfClosing { get; set; }

    /// <summary>
    /// Gets the list of HTML attributes attached to this start tag.
    /// </summary>
    public List<KeyValuePair<string, string>> Attributes { get; }

    protected internal override void ToString(StringBuilder builder)
    {
        builder.Append('<');
        builder.Append(TagName);

        foreach (var keyValuePair in Attributes)
        {
            builder.Append(' ');
            builder.Append(keyValuePair.Key);
            builder.Append("=\"");
            builder.Append(keyValuePair.Value);
            builder.Append('"');
        }

        if (IsSelfClosing)
        {
            builder.Append(" /");
        }
        builder.Append('>');
    }
}

/// <summary>
/// An HTML end comment tag.
/// </summary>
public class CppCommentHtmlEndTag : CppCommentHtmlTag
{
    /// <summary>
    /// Initializes a new instance of <see cref="CppCommentHtmlEndTag"/>.
    /// </summary>
    public CppCommentHtmlEndTag(CXComment comment) : base(comment, CppCommentKind.HtmlEndTag)
    {
    }

    protected internal override void ToString(StringBuilder builder)
    {
        builder.Append("</");
        builder.Append(TagName);
        builder.Append('>');
    }
}
