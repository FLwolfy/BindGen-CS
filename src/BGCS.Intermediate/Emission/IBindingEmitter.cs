using System.Collections.Generic;

namespace BGCS.Intermediate.Emission;

/// <summary>
/// Emits one backend representation from a fully analyzed binding module.
/// </summary>
public interface IBindingEmitter
{
    /// <summary>
    /// Gets the diagnostic name used to identify this output backend.
    /// </summary>
    string name { get; }

    /// <summary>
    /// Writes the analyzed module into the caller-owned staging directory.
    /// </summary>
    /// <param name = "module">
    /// The native ABI and managed presentation facts to emit.
    /// </param>
    /// <param name = "context">
    /// The output layout and runtime namespace for this emission.
    /// </param>
    /// <returns>
    /// The written source paths; an empty list means the backend contributes no files.
    /// </returns>
    IReadOnlyList<string> Emit(
        BindingModule module,
        EmissionContext context
    );
}
