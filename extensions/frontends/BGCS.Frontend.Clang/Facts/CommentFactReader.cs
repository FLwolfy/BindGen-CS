using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using BGCS.Core.Common;
using BGCS.Frontend.Clang.Model.Metadata;

namespace BGCS.Frontend.Clang.Facts;

internal static class CommentFactReader
{
    internal static SourceDocumentation? Read(CppComment? comment)
    {
        if (comment is null || comment.kind == CppCommentKind.Null) return null;
        List<string> summaries = [];
        List<string> returns = [];
        ImmutableArray<SourceParameterDocumentation>.Builder parameters = ImmutableArray.CreateBuilder<SourceParameterDocumentation>();
        ImmutableArray<SourceTypeParameterDocumentation>.Builder typeParameters = ImmutableArray.CreateBuilder<SourceTypeParameterDocumentation>();
        Visit(comment);
        return new(string.Join(Environment.NewLine, summaries).Trim(), parameters.ToImmutable(), returns.Count == 0 ? null : string.Join(Environment.NewLine, returns).Trim())
        { typeParameters = typeParameters.ToImmutable() };

        void Visit(CppComment node)
        {
            switch (node)
            {
                case CppCommentParamCommand parameter:
                    parameters.Add(new(parameter.paramName, parameter.isParamIndexValid ? parameter.paramIndex : null, parameter.ChildrenToString().Trim()));
                    break;
                case CppCommentTemplateParamCommand parameter:
                    typeParameters.Add(new(parameter.paramName, parameter.ChildrenToString().Trim()));
                    break;
                case CppCommentBlockCommand { commandName: "return" or "returns" or "result" } result:
                    returns.Add(result.ChildrenToString().Trim());
                    break;
                case CppCommentFull:
                    if (node.children is not null) foreach (CppComment child in node.children) Visit(child);
                    break;
                default:
                    string text = node.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(text)) summaries.Add(text);
                    break;
            }
        }
    }
}
