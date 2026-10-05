using System;
using Irony.Parsing;
using static BGCS.CppAst.AttributeParsing.NamedParameterParser;

namespace BGCS.CppAst.AttributeParsing;

/// <summary>
/// Defines the public class <c>NamedParameterGrammar</c>.
/// </summary>
[Language("NamedParameter.CppAst", "0.1", "Grammer for named parameter")]
internal sealed class NamedParameterGrammar : Grammar
{
    /// <summary>
    /// Executes public operation <c>new</c>.
    /// </summary>
    public static readonly NamedParameterGrammar instance = new();
    private NamedParameterGrammar() : base(true)
    {
        NumberLiteral NUMBER = CreateNumberLiteral(TerminalNames.C_NUMBER);
        StringLiteral STRING_LITERAL = new(TerminalNames.C_STRING, "\"", StringOptions.AllowsAllEscapes);
        IdentifierTerminal Name = new(TerminalNames.C_IDENTIFIER);
        //  Regular Operators
        var COMMA = ToTerm(TerminalNames.C_COMMA);
        var EQUAL = ToTerm(TerminalNames.C_EQUAL);
        var TRUE_KEYWORD = Keyword("true");
        var FALSE_KEYWORD = Keyword("false");
        var CLASS_KEYWORD = Keyword("__class");
        NonTerminal BOOLEAN = new(TerminalNames.C_BOOLEAN);
        NonTerminal EXPRESSION = new(TerminalNames.C_EXPRESSION);
        NonTerminal ASSIGNMENT = new(TerminalNames.C_ASSIGNMENT);
        NonTerminal NAMED_ARGUMENTS = new(TerminalNames.C_NAMEDARGUMENTS);
        NonTerminal LOOP_PAIR = new(TerminalNames.C_LOOPPAIR);
        NonTerminal ARGS = new(TerminalNames.C_ARGS);
        NonTerminal CLASS_NAME = new(TerminalNames.C_CLASSNAME);
        NonTerminal NAMESPACE = new(TerminalNames.C_NAMESPACE);
        NonTerminal TEMPLATE = new(TerminalNames.C_TEMPLATE);
        NonTerminal TEMPLATE_ELEM = new(TerminalNames.C_TEMPLATEELEM);
        NonTerminal CLASS = new(TerminalNames.C_CLASS);
        NonTerminal LEFT_BRACKET = new(TerminalNames.C_LEFTBRACKET);
        NonTerminal RIGHT_BRACKET = new(TerminalNames.C_RIGHTBRACKET);
        BOOLEAN.Rule = TRUE_KEYWORD | FALSE_KEYWORD;
        LEFT_BRACKET.Rule = ToTerm("(") | ToTerm("{");
        RIGHT_BRACKET.Rule = ToTerm(")") | ToTerm("}");
        NAMESPACE.Rule = MakePlusRule(NAMESPACE, ToTerm("::"), Name);
        TEMPLATE_ELEM.Rule = MakeStarRule(ARGS, ToTerm(","), Name | Empty);
        TEMPLATE.Rule = ToTerm("<") + TEMPLATE_ELEM + ToTerm(">");
        CLASS_NAME.Rule = NAMESPACE + TEMPLATE | Name + TEMPLATE | NAMESPACE | Name;
        ARGS.Rule = MakeStarRule(ARGS, ToTerm(","), EXPRESSION | Empty);
        CLASS.Rule = CLASS_KEYWORD + LEFT_BRACKET + CLASS_NAME + LEFT_BRACKET + ARGS + RIGHT_BRACKET + RIGHT_BRACKET;
        EXPRESSION.Rule = BOOLEAN | NUMBER | CLASS | STRING_LITERAL;
        ASSIGNMENT.Rule = Name | Name + EQUAL + EXPRESSION;
        LOOP_PAIR.Rule = MakeStarRule(COMMA + ASSIGNMENT);
        NAMED_ARGUMENTS.Rule = ASSIGNMENT + LOOP_PAIR;
        Root = NAMED_ARGUMENTS;
    }

    private BnfExpression MakeStarRule(BnfTerm term)
    {
        return MakeStarRule(new NonTerminal(term.Name + "*"), term);
    }

    /// <summary>
    /// Executes public operation <c>Keyword</c>.
    /// </summary>
    private KeyTerm Keyword(string keyword)
    {
        var term = ToTerm(keyword);
        MarkReservedWords(keyword);
        term.EditorInfo = new TokenEditorInfo(TokenType.Keyword, TokenColor.Keyword, TokenTriggers.None);
        return term;
    }

    /// <summary>
    /// Creates the numeric terminal used for named attribute arguments.
    /// </summary>
    /// <param name="name">Diagnostic name identifying the numeric terminal.</param>
    /// <returns>A terminal that recognizes integer values as Int32 and floating values as Double.</returns>
    private static NumberLiteral CreateNumberLiteral(string name)
    {
        NumberLiteral term = new(name)
        {
            //default int types are Integer (32bit) -> LongInteger (BigInt); Try Int64 before BigInt: Better performance?
            DefaultIntTypes = [TypeCode.Int32],
            DefaultFloatType = TypeCode.Double // it is default
        };
        return term;
    }
}
