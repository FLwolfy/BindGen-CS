using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Irony.Parsing;

namespace BGCS.CppAst.AttributeParsing;

/// <summary>
/// Parses annotation arguments into managed scalar values and normalized class-expression text.
/// </summary>
public static class NamedParameterParser
{
    #region Embeded Types
    /// <summary>
    /// Defines the public class <c>TerminalNames</c>.
    /// </summary>
    internal static class TerminalNames
    {
        /// <summary>
        /// Exposes public member <c>"identifier",</c>.
        /// </summary>
        public const string C_IDENTIFIER = "identifier", C_NUMBER = "number", C_STRING = "string", C_BOOLEAN = "boolean", C_COMMA = ",", C_EQUAL = "=", C_EXPRESSION = "expression", C_ASSIGNMENT = "assignment", C_LOOPPAIR = "loop_pair", C_NAMEDARGUMENTS = "named_arguments", C_ARGS = "args", C_CLASS = "class", C_CLASSNAME = "class_name", C_NAMESPACE = "namespace", C_TEMPLATE = "template", C_TEMPLATEELEM = "template_elem", C_LEFTBRACKET = "left_bracket", C_RIGHTBRACKET = "right_bracket";
    }

    #endregion Embeded Types
    /// <summary>
    /// Parses comma-separated named arguments, retaining the first value for each name.
    /// </summary>
    /// <param name="content">
    /// The annotation argument text; empty or whitespace text contributes no values.
    /// </param>
    /// <param name="outNamedParameterDic">
    /// The destination dictionary. Existing keys are preserved and successful parsing appends new keys.
    /// </param>
    /// <param name="errorMessage">
    /// Receives parser diagnostics on malformed input, or null on success.
    /// </param>
    /// <returns>
    /// True when the complete argument text is syntactically valid; false leaves the destination unchanged.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// The content or destination dictionary is null.
    /// </exception>
    public static bool ParseNamedParameters(
        string content,
        Dictionary<string, object> outNamedParameterDic,
        out string? errorMessage
    ) {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(outNamedParameterDic);
        errorMessage = null;
        if (string.IsNullOrWhiteSpace(content))
        {
            return true;
        }

        Parser parser = new(NamedParameterGrammar.instance);
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
            errorMessage = string.Join(Environment.NewLine, ast.ParserMessages.Select(message => message.ToString()));
        }

        return false;
    }

    private static object? ParseExpressionValue(ParseTreeNode node)
    {
        switch (node.Term.Name)
        {
            case TerminalNames.C_STRING:
                return node.Token.ValueString;
            case TerminalNames.C_BOOLEAN:
                if (node.ChildNodes[0].Term.Name == "false")
                {
                    return false;
                }

                return true;
            case TerminalNames.C_NUMBER:
                return node.Token.Value;
            case TerminalNames.C_TEMPLATE:
            case TerminalNames.C_CLASSNAME:
                return ParseNodeChildren(node);
            case TerminalNames.C_CLASS:
                return ParseClassToken(node.ChildNodes);
            case TerminalNames.C_ARGS:
                return ParseClassArgs(node.ChildNodes);
            case TerminalNames.C_NAMESPACE:
                return ParseNodeListWithSeparator(node.ChildNodes, "::");
            case TerminalNames.C_TEMPLATEELEM:
                return ParseNodeListWithSeparator(node.ChildNodes, ",");
            case TerminalNames.C_LEFTBRACKET:
            case TerminalNames.C_RIGHTBRACKET:
                return node.ChildNodes[0].Token.Value;
            default:
                if (node.ChildNodes.Count == 0 && node.Token != null)
                {
                    return node.Token.Value;
                }
                else if (node.ChildNodes.Count > 1)
                {
                    throw new InvalidOperationException($"Unexpected annotation expression node: {node.Term.Name}.");
                }

                return ParseExpressionValue(node.ChildNodes[0]);
        }
    }

    private static void ParseAssignment(
        ParseTreeNode node,
        Dictionary<string, object> outNamedParameterDic
    ) {
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

    private static void ParseLoopItem(
        ParseTreeNode loopNode,
        Dictionary<string, object> outNamedParameterDic
    ) {
        ParseAssignment(loopNode.ChildNodes[1], outNamedParameterDic);
        for (int i = 2; i < loopNode.ChildNodes.Count; i++)
        {
            ParseAssignment(loopNode.ChildNodes[i], outNamedParameterDic);
        }
    }

    private static string ParseNodeListWithSeparator(
        ParseTreeNodeList nodeList,
        string sep
    ) {
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
                realValue = Convert.ToString(nodeValue, CultureInfo.InvariantCulture);
            }

            if (builder.Length > 0)
            {
                builder.Append(',');
            }

            builder.Append(realValue);
        }

        return builder.ToString();
    }

    private static string? ParseClassToken(ParseTreeNodeList nodeList)
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

        return builder.ToString();
    }
}
