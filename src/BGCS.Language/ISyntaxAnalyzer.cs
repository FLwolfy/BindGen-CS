namespace BGCS.Language
{
    /// <summary>
    /// Defines the public interface <c>ISyntaxAnalyzer</c>.
    /// </summary>
    public interface ISyntaxAnalyzer
    {
        AnalyserResult Analyze(ParserContext context);
    }

    /// <summary>
    /// Defines the public interface <c>IMemberSyntaxAnalyzer</c>.
    /// </summary>
    public interface IMemberSyntaxAnalyzer
    {
        AnalyserResult Analyze(ParserContext context, IReadOnlyList<KeywordType> modifiers);
    }
}
