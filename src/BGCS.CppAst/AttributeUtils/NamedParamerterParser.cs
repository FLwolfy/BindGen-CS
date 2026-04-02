using System;
using System.Collections.Generic;
using Irony.Parsing;
using System.Text;

namespace BGCS.CppAst.AttributeUtils;
public class NamedParameterParser
{
    #region Embeded Types

    public static class TerminalNames
    {
        public const string Identifier = "identifier",
        Number = "number",
        String = "string",
        Boolean = "boolean",
        Comma = ",",
        Equal = "=",
        Expression = "expression",
        Assignment = "assignment",
        LoopPair = "loop_pair",
        NamedArguments = "named_arguments",
        Args = "args",
        Class = "class",
        ClassName = "class_name",
        NameSpace = "namespace",
        Template = "template",
        TemplateElem = "template_elem",
        LeftBracket = "left_bracket",
        RightBracket = "right_bracket";
    }

    #endregion Embeded Types

    public static bool ParseNamedParameters(string content, Dictionary<string, object> outNamedParameterDic, out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(content))
        {
            return true;
        }

        Parser parser = new(NamedParameterGrammar.Instance);
        var ast = parser.Parse(content);

        if (!ast.HasErrors())
        {
            ParseAssignment(ast.Root.ChildNodes[0], outNamedParameterDic);

            if (ast.Root.ChildNodes.Count >= 2 && ast.Root.ChildNodes[1].ChildNodes.Count > 0)
            {
                ParseLoopItem(ast.Root.ChildNodes[1].ChildNodes[0], outNamedParameterDic);
            }

            return true;
        }
        else
        {
            errorMessage = ast.ParserMessages.ToString();
        }

        return false;
    }

    private static object? ParseExpressionValue(ParseTreeNode node)
    {
        switch (node.Term.Name)
        {
            case TerminalNames.String:
                return node.Token.ValueString;

            case TerminalNames.Boolean:
                if (node.ChildNodes[0].Term.Name == "false")
                {
                    return false;
                }
                return true;

            case TerminalNames.Number:
                return node.Token.Value;

            case TerminalNames.Template:
            case TerminalNames.ClassName:
                return ParseNodeChildren(node);

            case TerminalNames.Class:
                return ParseClassToken(node.ChildNodes);

            case TerminalNames.Args:
                return ParseClassArgs(node.ChildNodes);

            case TerminalNames.NameSpace:
                return ParseNodeListWithSeparator(node.ChildNodes, "::");

            case TerminalNames.TemplateElem:
                return ParseNodeListWithSeparator(node.ChildNodes, ",");

            case TerminalNames.LeftBracket:
            case TerminalNames.RightBracket:
                return node.ChildNodes[0].Token.Value;

            default:
                if (node.ChildNodes.Count == 0 && node.Token != null)
                {
                    return node.Token.Value;
                }
                else if (node.ChildNodes.Count > 1)
                {
                    throw new Exception("Can not run to here!");
                }

                return ParseExpressionValue(node.ChildNodes[0]);
        }
    }

    private static void ParseAssignment(ParseTreeNode node, Dictionary<string, object> outNamedParameterDic)
    {
        string varName = node.ChildNodes[0].Token.ValueString;
        if (!outNamedParameterDic.ContainsKey(varName))
        {
            if (node.ChildNodes.Count == 1)
            {
                outNamedParameterDic.Add(varName, true);
            }
            else
            {
                var v = ParseExpressionValue(node.ChildNodes[2].ChildNodes[0]);
                if (v != null)
                {
                    outNamedParameterDic.Add(varName, v);
                }
            }
        }
    }

    private static void ParseLoopItem(ParseTreeNode loopNode, Dictionary<string, object> outNamedParameterDic)
    {
        ParseAssignment(loopNode.ChildNodes[1], outNamedParameterDic);

        for (int i = 2; i < loopNode.ChildNodes.Count; i++)
        {
            ParseAssignment(loopNode.ChildNodes[i], outNamedParameterDic);
        }
    }

    private static string ParseNodeListWithSeparator(ParseTreeNodeList nodeList, string sep)
    {
        StringBuilder builder = new();
        foreach (var node in nodeList)
        {
            if (builder.Length > 0)
            {
                builder.Append(sep);
            }

            builder.Append(ParseExpressionValue(node));
        }

        return builder.ToString();
    }

    private static string ParseNodeChildren(ParseTreeNode node)
    {
        StringBuilder builder = new();
        if (node.ChildNodes != null)
        {
            foreach (var child in node.ChildNodes)
            {
                builder.Append(ParseExpressionValue(child));
            }
        }

        return builder.ToString();
    }

    private static string ParseClassArgs(ParseTreeNodeList nodeList)
    {
        StringBuilder builder = new();

        foreach (var node in nodeList)
        {
            var nodeValue = ParseExpressionValue(node);
            object? realValue;
            if (nodeValue is string)
            {
                realValue = "\"" + nodeValue + "\"";
            }
            else if (nodeValue is bool)
            {
                var str = nodeValue.ToString();
                realValue = str == "True" ? "true" : "false";
            }
            else
            {
                realValue = nodeValue?.ToString();
            }

            if (builder.Length > 0)
            {
                builder.Append(',');
            }

            builder.Append(realValue);
        }

        return builder.ToString();
    }

    private static StringBuilder? ParseClassToken(ParseTreeNodeList nodeList)
    {
        if (nodeList.Count == 0)
        {
            return null;
        }

        StringBuilder builder = new();
        for (int i = 2; i < nodeList.Count - 1; i++)
        {
            builder.Append(ParseExpressionValue(nodeList[i]));
        }

        return builder;
    }
}
