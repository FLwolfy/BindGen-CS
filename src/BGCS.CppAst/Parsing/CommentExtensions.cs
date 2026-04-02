namespace BGCS.CppAst.Parsing;
using ClangSharp.Interop;
using BGCS.CppAst.Model.Metadata;
using BGCS.CppAst.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;

public static class CommentExtensions
{
    public static CppComment? GetComment(this in CXCursor cursor)
    {
        return cursor.ParsedComment.ToComment();
    }

    public static CppComment? ToComment(this in CXComment cxComment)
    {
        var cppKind = GetCommentKind(cxComment.Kind);

        CppComment cppComment;

        bool removeTrailingEmptyText = false;

        switch (cppKind)
        {
            case CppCommentKind.Null:
                return null;

            case CppCommentKind.Text:
                cppComment = new CppCommentText(cxComment)
                {
                    Text = CXUtil.GetComment_TextComment_Text(cxComment)?.TrimStart()
                };
                break;

            case CppCommentKind.InlineCommand:
                var inline = new CppCommentInlineCommand(cxComment);
                inline.CommandName = CXUtil.GetComment_InlineCommandComment_CommandName(cxComment);
                cppComment = inline;
                switch (cxComment.InlineCommandComment_RenderKind)
                {
                    case CXCommentInlineCommandRenderKind.CXCommentInlineCommandRenderKind_Normal:
                        inline.RenderKind = CppCommentInlineCommandRenderKind.Normal;
                        break;

                    case CXCommentInlineCommandRenderKind.CXCommentInlineCommandRenderKind_Bold:
                        inline.RenderKind = CppCommentInlineCommandRenderKind.Bold;
                        break;

                    case CXCommentInlineCommandRenderKind.CXCommentInlineCommandRenderKind_Monospaced:
                        inline.RenderKind = CppCommentInlineCommandRenderKind.Monospaced;
                        break;

                    case CXCommentInlineCommandRenderKind.CXCommentInlineCommandRenderKind_Emphasized:
                        inline.RenderKind = CppCommentInlineCommandRenderKind.Emphasized;
                        break;
                }

                for (uint i = 0; i < cxComment.InlineCommandComment_NumArgs; i++)
                {
                    inline.Arguments.Add(CXUtil.GetComment_InlineCommandComment_ArgText(cxComment, i));
                }
                break;

            case CppCommentKind.HtmlStartTag:
                CppCommentHtmlStartTag htmlStartTag = new(cxComment);
                htmlStartTag.TagName = CXUtil.GetComment_HtmlTagComment_TagName(cxComment);
                htmlStartTag.IsSelfClosing = cxComment.HtmlStartTagComment_IsSelfClosing;
                for (uint i = 0; i < cxComment.HtmlStartTag_NumAttrs; i++)
                {
                    htmlStartTag.Attributes.Add(new KeyValuePair<string, string>(
                        CXUtil.GetComment_HtmlStartTag_AttrName(cxComment, i),
                        CXUtil.GetComment_HtmlStartTag_AttrValue(cxComment, i)
                        ));
                }
                cppComment = htmlStartTag;
                break;

            case CppCommentKind.HtmlEndTag:
                CppCommentHtmlEndTag htmlEndTag = new(cxComment);
                htmlEndTag.TagName = CXUtil.GetComment_HtmlTagComment_TagName(cxComment);
                cppComment = htmlEndTag;
                break;

            case CppCommentKind.Paragraph:
                cppComment = new CppCommentParagraph(cxComment);
                break;

            case CppCommentKind.BlockCommand:
                CppCommentBlockCommand blockComment = new(cxComment);
                blockComment.CommandName = CXUtil.GetComment_BlockCommandComment_CommandName(cxComment);
                for (uint i = 0; i < cxComment.BlockCommandComment_NumArgs; i++)
                {
                    blockComment.Arguments.Add(CXUtil.GetComment_BlockCommandComment_ArgText(cxComment, i));
                }

                removeTrailingEmptyText = true;
                cppComment = blockComment;
                break;

            case CppCommentKind.ParamCommand:
                CppCommentParamCommand paramComment = new(cxComment);
                paramComment.CommandName = "param";
                paramComment.ParamName = CXUtil.GetComment_ParamCommandComment_ParamName(cxComment);
                paramComment.IsDirectionExplicit = cxComment.ParamCommandComment_IsDirectionExplicit;
                paramComment.IsParamIndexValid = cxComment.ParamCommandComment_IsParamIndexValid;
                paramComment.ParamIndex = (int)cxComment.ParamCommandComment_ParamIndex;
                switch (cxComment.ParamCommandComment_Direction)
                {
                    case CXCommentParamPassDirection.CXCommentParamPassDirection_In:
                        paramComment.Direction = CppCommentParamDirection.In;
                        break;

                    case CXCommentParamPassDirection.CXCommentParamPassDirection_Out:
                        paramComment.Direction = CppCommentParamDirection.Out;
                        break;

                    case CXCommentParamPassDirection.CXCommentParamPassDirection_InOut:
                        paramComment.Direction = CppCommentParamDirection.InOut;
                        break;
                }

                removeTrailingEmptyText = true;
                cppComment = paramComment;
                break;

            case CppCommentKind.TemplateParamCommand:
                CppCommentTemplateParamCommand tParamComment = new(cxComment);
                tParamComment.CommandName = "tparam";
                tParamComment.ParamName = CXUtil.GetComment_TParamCommandComment_ParamName(cxComment);
                tParamComment.Depth = (int)cxComment.TParamCommandComment_Depth;
                // TODO: index
                tParamComment.IsPositionValid = cxComment.TParamCommandComment_IsParamPositionValid;

                removeTrailingEmptyText = true;
                cppComment = tParamComment;
                break;

            case CppCommentKind.VerbatimBlockCommand:
                CppCommentVerbatimBlockCommand verbatimBlock = new(cxComment);
                verbatimBlock.CommandName = CXUtil.GetComment_BlockCommandComment_CommandName(cxComment);
                for (uint i = 0; i < cxComment.BlockCommandComment_NumArgs; i++)
                {
                    verbatimBlock.Arguments.Add(CXUtil.GetComment_BlockCommandComment_ArgText(cxComment, i));
                }
                cppComment = verbatimBlock;
                break;

            case CppCommentKind.VerbatimBlockLine:
                var text = CXUtil.GetComment_VerbatimBlockLineComment_Text(cxComment);

                // For some reason, VerbatimBlockLineComment_Text can return the rest of the file instead of just the line
                // So we explicitly trim the line here
                var indexOfLine = text.IndexOf('\n');
                if (indexOfLine >= 0)
                {
                    text = text.Substring(0, indexOfLine);
                }

                cppComment = new CppCommentVerbatimBlockLine(cxComment)
                {
                    Text = text
                };
                break;

            case CppCommentKind.VerbatimLine:
                cppComment = new CppCommentVerbatimLine(cxComment)
                {
                    Text = CXUtil.GetComment_VerbatimLineComment_Text(cxComment)
                };
                break;

            case CppCommentKind.Full:
                cppComment = new CppCommentFull(cxComment);
                break;

            default:
                return null;
        }

        Debug.Assert(cppComment != null);

        for (uint i = 0; i < cxComment.NumChildren; i++)
        {
            var cxChildComment = cxComment.GetChild(i);
            var cppChildComment = cxChildComment.ToComment();
            if (cppChildComment != null)
            {
                cppComment.Children ??= [];
                cppComment.Children.Add(cppChildComment);
            }
        }

        if (removeTrailingEmptyText)
        {
            RemoveTrailingEmptyText(cppComment);
        }

        return cppComment;
    }

    private static void RemoveTrailingEmptyText(CppComment cppComment)
    {
        // Remove the last paragraph if it is an empty string text
        if (cppComment.Children != null && cppComment.Children.Count > 0 && cppComment.Children[cppComment.Children.Count - 1] is CppCommentParagraph paragraph)
        {
            // Remove the last paragraph if it is an empty string text
            if (paragraph.Children != null && paragraph.Children.Count > 0 && paragraph.Children[paragraph.Children.Count - 1] is CppCommentText text && string.IsNullOrWhiteSpace(text.Text))
            {
                paragraph.Children.RemoveAt(paragraph.Children.Count - 1);
            }
        }
    }

    private static CppCommentKind GetCommentKind(CXCommentKind kind)
    {
        return kind switch
        {
            CXCommentKind.CXComment_Null => CppCommentKind.Null,
            CXCommentKind.CXComment_Text => CppCommentKind.Text,
            CXCommentKind.CXComment_InlineCommand => CppCommentKind.InlineCommand,
            CXCommentKind.CXComment_HTMLStartTag => CppCommentKind.HtmlStartTag,
            CXCommentKind.CXComment_HTMLEndTag => CppCommentKind.HtmlEndTag,
            CXCommentKind.CXComment_Paragraph => CppCommentKind.Paragraph,
            CXCommentKind.CXComment_BlockCommand => CppCommentKind.BlockCommand,
            CXCommentKind.CXComment_ParamCommand => CppCommentKind.ParamCommand,
            CXCommentKind.CXComment_TParamCommand => CppCommentKind.TemplateParamCommand,
            CXCommentKind.CXComment_VerbatimBlockCommand => CppCommentKind.VerbatimBlockCommand,
            CXCommentKind.CXComment_VerbatimBlockLine => CppCommentKind.VerbatimBlockLine,
            CXCommentKind.CXComment_VerbatimLine => CppCommentKind.VerbatimLine,
            CXCommentKind.CXComment_FullComment => CppCommentKind.Full,
            _ => throw new ArgumentOutOfRangeException($"Unsupported comment kind `{kind}`"),
        };
    }
}
