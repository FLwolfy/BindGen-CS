using System.Collections.Generic;
using BGCS.Language.Lexing;

namespace BGCS.Language.Parsing
{
    /// <summary>
    /// Consumes members after the containing analyzer has collected declaration modifiers.
    /// </summary>
    public interface IMemberSyntaxAnalyzer
    {

        /// <summary>
        /// Analyzes a member after its declaration modifiers have been collected.
        /// </summary>
        /// <param name="context">Mutable context owned by the active parser operation.</param>
        /// <param name="modifiers">Declaration modifiers preceding the current member.</param>
        /// <returns>The analyzer outcome; successful analysis advances the context past the member.</returns>
        AnalyserResult Analyze(
            ParserContext context,
            IReadOnlyList<KeywordType> modifiers
        );
    }
}
