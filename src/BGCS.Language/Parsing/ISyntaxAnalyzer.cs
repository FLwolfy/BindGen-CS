namespace BGCS.Language.Parsing
{
    /// <summary>
    /// Consumes declarations from a shared parser context without owning the complete parser pipeline.
    /// </summary>
    public interface ISyntaxAnalyzer
    {

        /// <summary>
        /// Attempts to consume the current token sequence and add its syntax to the parser context.
        /// </summary>
        /// <param name="context">Mutable context owned by the active parser operation.</param>
        /// <returns>Success after consuming a declaration, or the analyzer outcome describing why it could not proceed.</returns>
        AnalyserResult Analyze(ParserContext context);
    }

}
