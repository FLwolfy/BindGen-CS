using BGCS.Analysis;
using BGCS.Configuration;
namespace BGCS.Patching;

/// <summary>
/// Transforms the attempt-local native analysis model before it is lowered into binding IR.
/// </summary>
public interface IPrePatch
{
    /// <summary>
    /// Updates generation policy or borrowed native declarations without rewriting original source files.
    /// </summary>
    /// <param name="settings">
    /// The active generation policy to update for this attempt.
    /// </param>
    /// <param name="result">
    /// The borrowed parse model; its native objects must not be retained beyond this attempt.
    /// </param>
    void Apply(
        CsCodeGeneratorConfig settings,
        ParseResult result
    );
}
