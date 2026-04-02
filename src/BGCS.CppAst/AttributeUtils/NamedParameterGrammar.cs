using System;
using Irony.Parsing;
using static BGCS.CppAst.AttributeUtils.NamedParameterParser;

namespace BGCS.CppAst.AttributeUtils;
[Language("NamedParameter.CppAst", "0.1", "Grammer for named parameter")]
public class NamedParameterGrammar : Grammar
{
    public static readonly NamedParameterGrammar Instance = new();

    private NamedParameterGrammar() :
        base(true)
    {
        #region Declare Terminals Here

        NumberLiteral NUMBER = CreateNumberLiteral(TerminalNames.Number);
        StringLiteral STRING_LITERAL = new(TerminalNames.String, "\"", StringOptions.AllowsAllEscapes);
        IdentifierTerminal Name = new(TerminalNames.Identifier);

        //  Regular Operators
        var COMMA = ToTerm(TerminalNames.Comma);
        var EQUAL = ToTerm(TerminalNames.Equal);

        #region Keywords

        var TRUE_KEYWORD = Keyword("true");
        var FALSE_KEYWORD = Keyword("false");

        var CLASS_KEYWORD = Keyword("__class");
        //var NEW = Keyword("new");

        #endregion Keywords

        #endregion Declare Terminals Here

        #region Declare NonTerminals Here

        NonTerminal BOOLEAN = new(TerminalNames.Boolean);
        NonTerminal EXPRESSION = new(TerminalNames.Expression);
        NonTerminal ASSIGNMENT = new(TerminalNames.Assignment);
        NonTerminal NAMED_ARGUMENTS = new(TerminalNames.NamedArguments);
        NonTerminal LOOP_PAIR = new(TerminalNames.LoopPair);
        NonTerminal ARGS = new(TerminalNames.Args);
        NonTerminal CLASS_NAME = new(TerminalNames.ClassName);
        NonTerminal NAMESPACE = new(TerminalNames.NameSpace);
        NonTerminal TEMPLATE = new(TerminalNames.Template);
        NonTerminal TEMPLATE_ELEM = new(TerminalNames.TemplateElem);
        NonTerminal CLASS = new(TerminalNames.Class);
        NonTerminal LEFT_BRACKET = new(TerminalNames.LeftBracket);
        NonTerminal RIGHT_BRACKET = new(TerminalNames.RightBracket);

        #endregion Declare NonTerminals Here

        #region Place Rules Here

        ////NORMAL_RECORD.Rule = Name + FIELD_FETCH;

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

        #endregion Place Rules Here

        #region Define Keywords and Register Symbols

        ////this.RegisterBracePair("[", "]");

        ////this.MarkPunctuation(",", ";");

        #endregion Define Keywords and Register Symbols
    }

    //Must create new overrides here in order to support the "Operator" token color
    public new void RegisterOperators(int precedence, params string[] opSymbols)
    {
        RegisterOperators(precedence, Associativity.Left, opSymbols);
    }

    //Must create new overrides here in order to support the "Operator" token color
    public new void RegisterOperators(int precedence, Associativity associativity, params string[] opSymbols)
    {
        foreach (string op in opSymbols)
        {
            KeyTerm opSymbol = Operator(op);
            opSymbol.Precedence = precedence;
            opSymbol.Associativity = associativity;
        }
    }

    private BnfExpression MakeStarRule(BnfTerm term)
    {
        return MakeStarRule(new NonTerminal(term.Name + "*"), term);
    }

    public KeyTerm Keyword(string keyword)
    {
        var term = ToTerm(keyword);
        // term.SetOption(TermOptions.IsKeyword, true);
        // term.SetOption(TermOptions.IsReservedWord, true);

        MarkReservedWords(keyword);
        term.EditorInfo = new TokenEditorInfo(TokenType.Keyword, TokenColor.Keyword, TokenTriggers.None);

        return term;
    }

    public KeyTerm Operator(string op)
    {
        string opCased = CaseSensitive ? op : op.ToLower();
        KeyTerm term = new(opCased, op)
        {
            EditorInfo = new TokenEditorInfo(TokenType.Operator, TokenColor.Keyword, TokenTriggers.None)
        };
        //term.SetOption(TermOptions.IsOperator, true);

        return term;
    }

    protected static NumberLiteral CreateNumberLiteral(string name)
    {
        NumberLiteral term = new(name)
        {
            //default int types are Integer (32bit) -> LongInteger (BigInt); Try Int64 before BigInt: Better performance?
            DefaultIntTypes = [TypeCode.Int32],
            DefaultFloatType = TypeCode.Double // it is default
        };
        ////term.AddPrefix("0x", NumberOptions.Hex);

        return term;
    }
}
