using System.Diagnostics.CodeAnalysis;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Metadata;

namespace BGCS.Analysis;

/// <summary>
/// Resolves macro declarations within the captured native compilation.
/// </summary>
public static class CppMacroExtensions
{
    /// <summary>
    /// Finds the first macro whose modeled name exactly matches the requested identifier.
    /// </summary>
    /// <param name="compilation">
    /// The compilation owning the macro descriptors.
    /// </param>
    /// <param name="name">
    /// The ordinal native macro identifier to find.
    /// </param>
    /// <returns>
    /// The borrowed descriptor, or null when no macro has that exact name.
    /// </returns>
    public static CppMacro? FindMacro(
        this CppCompilation compilation,
        string name
    ) {
        for (int i = 0; i < compilation.macros.Count; i++)
        {
            var macro = compilation.macros[i];
            if (macro.name == name)
                return macro;
        }

        return null;
    }

    /// <summary>
    /// Finds the first exact-name macro without requiring a null check at the call site.
    /// </summary>
    /// <param name="compilation">
    /// The compilation owning the macro descriptors.
    /// </param>
    /// <param name="name">
    /// The ordinal native macro identifier to find.
    /// </param>
    /// <param name="macro">
    /// Receives the borrowed descriptor on success, or null on failure.
    /// </param>
    /// <returns>
    /// True when a matching macro exists.
    /// </returns>
    public static bool TryFindMacro(
        this CppCompilation compilation,
        string name,
        [NotNullWhen(true)] out CppMacro? macro
    ) {
        macro = FindMacro(compilation, name);
        return macro != null;
    }
}
