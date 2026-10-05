using BGCS.Core.Text;
using BGCS.Language.Lexing;
using BGCS.Language.Syntax;

namespace BGCS.Language.Cpp.Nodes;

/// <summary>
/// Retains the source spelling and lexical classifications of a literal in a macro expression.
/// </summary>
public class ValueNode : SyntaxNode
{
    /// <summary>
    /// Retains a literal spelling without evaluating, decoding, or range-checking its value.
    /// </summary>
    /// <param name="value">
    /// The literal spelling without any quote delimiters.
    /// </param>
    /// <param name="type">
    /// The broad literal category.
    /// </param>
    /// <param name="numberType">
    /// The numeric carrier category, meaningful only for numeric literals.
    /// </param>
    public ValueNode(
        string value,
        LiteralType type,
        NumberType numberType
    ) {
        this.value = value;
        this.type = type;
        this.numberType = numberType;
    }

    /// <summary>
    /// Gets the literal source spelling without any quote delimiters.
    /// </summary>
    public string value { get; }
    /// <summary>
    /// Gets the broad literal category recorded by tokenization.
    /// </summary>
    public LiteralType type { get; }
    /// <summary>
    /// Gets the numeric carrier category; nonnumeric literals may carry None.
    /// </summary>
    public NumberType numberType { get; }

    /// <summary>
    /// Formats the literal spelling for syntax-tree diagnostics.
    /// </summary>
    /// <returns>
    /// The value label followed by the retained source spelling.
    /// </returns>
    public override string ToString()
    {
        return $"value: {this.value}";
    }
}
