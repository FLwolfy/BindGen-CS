using System.Collections.Generic;
using System.Text;
using BGCS.CppAst.Model.Attributes;
using BGCS.CppAst.Model.Interfaces;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using ClangSharp.Interop;

namespace BGCS.CppAst.Model.Metadata;

/// <summary>
/// Top level comment container.
/// </summary>
public class CppCommentFull : CppComment
{
    /// <summary>
    /// Initializes a new instance of <see cref = "CppCommentFull"/>.
    /// </summary>
    /// <param name="comment">Native documentation node; valid only while its compilation is alive.</param>
    public CppCommentFull(CXComment comment) : base(comment, CppCommentKind.Full)
    {
    }

    /// <inheritdoc/>
    protected internal override void ToString(StringBuilder builder)
    {
        ChildrenToString(builder);
    }

    /// <inheritdoc/>
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
    /// <summary>
    /// Initializes a documentation node borrowing its native comment from the current compilation.
    /// </summary>
    /// <param name="comment">Native documentation node; valid only while its compilation is alive.</param>
    /// <param name="kind">Documentation category represented by this node.</param>
    protected CppComment(
        CXComment comment,
        CppCommentKind kind
    ) {
        this.comment = comment;
        this.kind = kind;
    }

    /// <summary>
    /// Gets or sets the borrowed native comment; its owning compilation must remain alive.
    /// </summary>
    public CXComment comment { get; set; }
    /// <summary>
    /// The kind of comments.
    /// </summary>
    public CppCommentKind kind { get; }
    /// <summary>
    /// Gets a list of children. Might be null.
    /// </summary>
    public List<CppComment>? children { get; set; }

    /// <summary>
    /// Appends this documentation node to the caller's builder.
    /// </summary>
    /// <param name="builder">Destination builder; implementations append without clearing it.</param>
    protected internal abstract void ToString(StringBuilder builder);
    /// <summary>
    /// Appends child documentation in declaration order.
    /// </summary>
    /// <param name="builder">Destination text builder owned by the caller.</param>
    protected void ChildrenToString(StringBuilder builder)
    {
        if (this.children != null)
        {
            foreach (var children in this.children)
            {
                children.ToString(builder);
            }
        }
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        var builder = new StringBuilder();
        ToString(builder);
        return builder.ToString();
    }

    /// <summary>
    /// Renders child documentation in declaration order.
    /// </summary>
    /// <returns>The rendered children, or an empty string when no children exist.</returns>
    public string ChildrenToString()
    {
        var builder = new StringBuilder();
        ChildrenToString(builder);
        return builder.ToString();
    }

    /// <summary>
    /// Adds supported double-bracket attribute declarations found in documentation to a declaration.
    /// </summary>
    /// <param name="attrContainer">Declaration receiving successfully parsed attributes.</param>
    public void TryToParseAttributes(ICppAttributeContainer attrContainer)
    {
        if (this is CppCommentText ctxt && ctxt.text != null)
        {
            var txt = ctxt.text.Trim();
            if (txt.StartsWith("[[") && txt.EndsWith("]]"))
            {
                attrContainer.attributes.Add(new CppAttribute(this.comment, "comment", AttributeKind.CommentAttribute) { arguments = txt, scope = "", isVariadic = false, });
            }
        }

        if (this.children != null)
        {
            foreach (var child in this.children)
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
    /// <summary>
    /// Initializes a documentation node borrowing its native comment from the current compilation.
    /// </summary>
    /// <param name="comment">Native documentation node; valid only while its compilation is alive.</param>
    /// <param name="kind">Documentation category represented by this node.</param>
    protected CppCommentCommand(
        CXComment comment,
        CppCommentKind kind
    ) : base(comment, kind)
    {
        this.arguments = [];
    }

    /// <summary>
    /// Gets or sets the native documentation command identifier without its introducer.
    /// </summary>
    public string commandName { get; set; } = string.Empty;
    /// <summary>
    /// Gets mutable documentation-command arguments in source order.
    /// </summary>
    public List<string> arguments { get; }

    /// <inheritdoc/>
    protected internal override void ToString(StringBuilder builder)
    {
        builder.Append($"@{this.commandName}");
        for (var index = 0; index < this.arguments.Count; index++)
        {
            var argument = this.arguments[index];
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
    /// Initializes a new instance of <see cref = "CppCommentParagraph"/>.
    /// </summary>
    /// <param name="comment">Native documentation node; valid only while its compilation is alive.</param>
    public CppCommentParagraph(CXComment comment) : base(comment, CppCommentKind.Paragraph)
    {
    }

    /// <inheritdoc/>
    protected internal override void ToString(StringBuilder builder)
    {
        if (this.children != null)
        {
            for (var i = 0; i < this.children.Count; i++)
            {
                var children = this.children[i];
                children.ToString(builder);
                // If a text is followed by a text, we assume that it was a new line
                // between the two
                if (children.kind == CppCommentKind.Text && i + 1 < this.children.Count && this.children[i + 1].kind == CppCommentKind.Text)
                {
                    var text = ((CppCommentText)children).text;
                    var nextText = ((CppCommentText)children).text;
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
    /// Initializes a new instance of <see cref = "CppCommentBlockCommand"/>.
    /// </summary>
    /// <param name="comment">Native documentation node; valid only while its compilation is alive.</param>
    public CppCommentBlockCommand(CXComment comment) : base(comment, CppCommentKind.BlockCommand)
    {
    }

    /// <inheritdoc/>
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
    /// Initializes a new instance of <see cref = "CppCommentInlineCommand"/>.
    /// </summary>
    /// <param name="comment">Native documentation node; valid only while its compilation is alive.</param>
    public CppCommentInlineCommand(CXComment comment) : base(comment, CppCommentKind.InlineCommand)
    {
    }

    /// <summary>
    /// Gets or sets the inline documentation command presentation selected by Clang.
    /// </summary>
    public CppCommentInlineCommandRenderKind renderKind { get; set; }

    /// <inheritdoc/>
    protected internal override void ToString(StringBuilder builder)
    {
        base.ToString(builder);
        ChildrenToString(builder);
    }
}

/// <summary>
/// Type of rendering for an <see cref = "CppCommentInlineCommand"/>
/// </summary>
public enum CppCommentInlineCommandRenderKind
{
    /// <summary>
    /// Renders an inline command without emphasis.
    /// </summary>
    Normal,
    /// <summary>
    /// Renders an inline command in bold text.
    /// </summary>
    Bold,
    /// <summary>
    /// Renders an inline command using fixed-width text.
    /// </summary>
    Monospaced,
    /// <summary>
    /// Renders an inline command with emphasis.
    /// </summary>
    Emphasized,
}

/// <summary>
/// A comment for a function/method parameter.
/// </summary>
public class CppCommentParamCommand : CppCommentCommand
{
    /// <summary>
    /// Initializes a new instance of <see cref = "CppCommentParamCommand"/>.
    /// </summary>
    /// <param name="comment">Native documentation node; valid only while its compilation is alive.</param>
    public CppCommentParamCommand(CXComment comment) : base(comment, CppCommentKind.ParamCommand)
    {
    }

    /// <summary>
    /// Gets or sets the name of the parameter.
    /// </summary>
    public string paramName { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets a boolean indicating if the <see cref = "paramIndex"/> is valid.
    /// </summary>
    public bool isParamIndexValid { get; set; }
    /// <summary>
    /// Gets or sets the index of this parameter in the function parameters.
    /// </summary>
    public int paramIndex { get; set; }
    /// <summary>
    /// Gets or sets the direction of this parameter (in, out, inout).
    /// </summary>
    public CppCommentParamDirection direction { get; set; }
    /// <summary>
    /// Gets or sets a boolean indicating if <see cref = "direction"/> was explicitly specified.
    /// </summary>
    public bool isDirectionExplicit { get; set; }

    /// <inheritdoc/>
    protected internal override void ToString(StringBuilder builder)
    {
        base.ToString(builder);
        builder.Append(this.paramName);
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
    /// Initializes a new instance of <see cref = "CppCommentTemplateParamCommand"/>.
    /// </summary>
    /// <param name="comment">Native documentation node; valid only while its compilation is alive.</param>
    public CppCommentTemplateParamCommand(CXComment comment) : base(comment, CppCommentKind.TemplateParamCommand)
    {
    }

    /// <summary>
    /// Gets or sets the name of the parameter.
    /// </summary>
    public string paramName { get; set; } = string.Empty;
    /// <summary>
    /// Depth or this parameter.
    /// </summary>
    public int depth { get; set; }
    /// <summary>
    /// Gets or sets a boolean indicating if this <see cref = "index"/> is valid
    /// </summary>
    public bool isPositionValid { get; set; }
    /// <summary>
    /// Gets or sets the index of this template parameter.
    /// </summary>
    public int index { get; set; }

    /// <inheritdoc/>
    protected internal override void ToString(StringBuilder builder)
    {
        base.ToString(builder);
        builder.Append(this.paramName);
        builder.Append(' ');
        ChildrenToString(builder);
    }
}

/// <summary>
/// Direction used by <see cref = "CppCommentParamCommand"/>
/// </summary>
public enum CppCommentParamDirection
{
    /// <summary>
    /// Documents an input parameter.
    /// </summary>
    In,
    /// <summary>
    /// Documents an output parameter.
    /// </summary>
    Out,
    /// <summary>
    /// Documents a parameter used for both input and output.
    /// </summary>
    InOut,
}

/// <summary>
/// An enumeration for <see cref = "CppComment"/>
/// </summary>
public enum CppCommentKind
{
    /// <summary>
    /// An absent documentation node.
    /// </summary>
    Null = 0,
    /// <summary>
    /// Plain documentation text.
    /// </summary>
    Text = 1,
    /// <summary>
    /// An inline documentation command.
    /// </summary>
    InlineCommand = 2,
    /// <summary>
    /// An opening HTML tag.
    /// </summary>
    HtmlStartTag = 3,
    /// <summary>
    /// A closing HTML tag.
    /// </summary>
    HtmlEndTag = 4,
    /// <summary>
    /// A paragraph containing documentation nodes.
    /// </summary>
    Paragraph = 5,
    /// <summary>
    /// A block documentation command.
    /// </summary>
    BlockCommand = 6,
    /// <summary>
    /// Documentation for a function parameter.
    /// </summary>
    ParamCommand = 7,
    /// <summary>
    /// Documentation for a template parameter.
    /// </summary>
    TemplateParamCommand = 8,
    /// <summary>
    /// A block containing literal documentation text.
    /// </summary>
    VerbatimBlockCommand = 9,
    /// <summary>
    /// A literal line inside a verbatim block.
    /// </summary>
    VerbatimBlockLine = 10,
    /// <summary>
    /// A standalone literal documentation line.
    /// </summary>
    VerbatimLine = 11,
    /// <summary>
    /// The root container for a complete documentation comment.
    /// </summary>
    Full = 12,
}

/// <summary>
/// A comment for a verbatim block command.
/// </summary>
public class CppCommentVerbatimBlockCommand : CppCommentCommand
{
    /// <summary>
    /// Initializes a new instance of <see cref = "CppCommentVerbatimBlockCommand"/>.
    /// </summary>
    /// <param name="comment">Native documentation node; valid only while its compilation is alive.</param>
    public CppCommentVerbatimBlockCommand(CXComment comment) : base(comment, CppCommentKind.VerbatimBlockCommand)
    {
    }

    /// <inheritdoc/>
    protected internal override void ToString(StringBuilder builder)
    {
        base.ToString(builder);
        ChildrenToString(builder);
        builder.AppendLine($"@end{this.commandName}");
    }
}

/// <summary>
/// A comment for a verbatim line inside a verbatim block.
/// </summary>
public class CppCommentVerbatimBlockLine : CppCommentTextBase
{
    /// <summary>
    /// Initializes a new instance of <see cref = "CppCommentVerbatimBlockLine"/>.
    /// </summary>
    /// <param name="comment">Native documentation node; valid only while its compilation is alive.</param>
    public CppCommentVerbatimBlockLine(CXComment comment) : base(comment, CppCommentKind.VerbatimBlockLine)
    {
    }

    /// <inheritdoc/>
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
    /// <summary>
    /// Initializes a documentation node borrowing its native comment from the current compilation.
    /// </summary>
    /// <param name="comment">Native documentation node; valid only while its compilation is alive.</param>
    /// <param name="kind">Documentation category represented by this node.</param>
    protected CppCommentTextBase(
        CXComment comment,
        CppCommentKind kind
    ) : base(comment, kind)
    {
    }

    /// <summary>
    /// Gets or sets the documentation text fragment, or null when no text was captured.
    /// </summary>
    public string? text { get; set; }

    /// <inheritdoc/>
    protected internal override void ToString(StringBuilder builder)
    {
        builder.Append(this.text);
    }
}

/// <summary>
/// A simple text comment entry.
/// </summary>
public class CppCommentText : CppCommentTextBase
{
    /// <summary>
    /// Initializes a new instance of <see cref = "CppCommentText"/>.
    /// </summary>
    /// <param name="comment">Native documentation node; valid only while its compilation is alive.</param>
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
    /// Initializes a new instance of <see cref = "CppCommentVerbatimLine"/>.
    /// </summary>
    /// <param name="comment">Native documentation node; valid only while its compilation is alive.</param>
    public CppCommentVerbatimLine(CXComment comment) : base(comment, CppCommentKind.VerbatimLine)
    {
    }

    /// <inheritdoc/>
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
    /// <summary>
    /// Initializes a documentation node borrowing its native comment from the current compilation.
    /// </summary>
    /// <param name="comment">Native documentation node; valid only while its compilation is alive.</param>
    /// <param name="kind">Documentation category represented by this node.</param>
    protected CppCommentHtmlTag(
        CXComment comment,
        CppCommentKind kind
    ) : base(comment, kind)
    {
    }

    /// <summary>
    /// Gets or sets the native documentation HTML tag identifier without delimiters.
    /// </summary>
    public string tagName { get; set; } = string.Empty;

    /// <inheritdoc/>
    protected internal abstract override void ToString(StringBuilder builder);
}

/// <summary>
/// An HTML start comment tag.
/// </summary>
public class CppCommentHtmlStartTag : CppCommentHtmlTag
{
    /// <summary>
    /// Initializes a new instance of <see cref = "CppCommentHtmlStartTag"/>.
    /// </summary>
    /// <param name="comment">Native documentation node; valid only while its compilation is alive.</param>
    public CppCommentHtmlStartTag(CXComment comment) : base(comment, CppCommentKind.HtmlStartTag)
    {
        this.attributes = [];
    }

    /// <summary>
    /// Gets or sets a boolean indicating if this start tag is self closing.
    /// </summary>
    public bool isSelfClosing { get; set; }
    /// <summary>
    /// Gets the list of HTML attributes attached to this start tag.
    /// </summary>
    public List<KeyValuePair<string, string>> attributes { get; }

    /// <inheritdoc/>
    protected internal override void ToString(StringBuilder builder)
    {
        builder.Append('<');
        builder.Append(this.tagName);
        foreach (var keyValuePair in this.attributes)
        {
            builder.Append(' ');
            builder.Append(keyValuePair.Key);
            builder.Append("=\"");
            builder.Append(keyValuePair.Value);
            builder.Append('"');
        }

        if (this.isSelfClosing)
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
    /// Initializes a new instance of <see cref = "CppCommentHtmlEndTag"/>.
    /// </summary>
    /// <param name="comment">Native documentation node; valid only while its compilation is alive.</param>
    public CppCommentHtmlEndTag(CXComment comment) : base(comment, CppCommentKind.HtmlEndTag)
    {
    }

    /// <inheritdoc/>
    protected internal override void ToString(StringBuilder builder)
    {
        builder.Append("</");
        builder.Append(this.tagName);
        builder.Append('>');
    }
}
