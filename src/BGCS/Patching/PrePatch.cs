using BGCS.Analysis;
using BGCS.Configuration;

namespace BGCS.Patching
{
    using BGCS.CppAst.Model.Declarations;

    /// <summary>
    /// Dispatches attempt-local native declarations to optional pre-analysis transformation hooks.
    /// </summary>
    public abstract class PrePatch : IPrePatch
    {
        /// <summary>
        /// Transforms borrowed native declarations before binding analysis without rewriting source files.
        /// </summary>
        /// <param name="settings">
        /// The active mutable generation policy.
        /// </param>
        /// <param name="result">
        /// The analysis model borrowed for this attempt; native declarations must not escape its lifetime.
        /// </param>
        public virtual void Apply(
            CsCodeGeneratorConfig settings,
            ParseResult result
        ) {
            PatchCompilation(settings, result);
        }

        /// <summary>
        /// Dispatches supported declarations to their per-declaration transformation hooks.
        /// </summary>
        /// <param name="settings">Active generation configuration.</param>
        /// <param name="result">Borrowed parse result for the current attempt.</param>
        protected virtual void PatchCompilation(
            CsCodeGeneratorConfig settings,
            ParseResult result
        ) {
            var compilation = result.compilation;
            foreach (var type in compilation.classes)
            {
                PatchClass(settings, type);
            }

            foreach (var type in compilation.typedefs)
            {
                PatchTypedef(settings, type);
            }

            foreach (var type in compilation.functions)
            {
                PatchFunction(settings, type);
            }

            foreach (var type in compilation.enums)
            {
                PatchEnum(settings, type);
            }
        }

        /// <summary>
        /// Transforms one borrowed native record before binding analysis.
        /// </summary>
        /// <param name="settings">Active generation configuration.</param>
        /// <param name="cppClass">Mutable declaration valid only while the current compilation remains alive.</param>
        protected virtual void PatchClass(
            CsCodeGeneratorConfig settings,
            CppClass cppClass
        ) {
        }

        /// <summary>
        /// Transforms one borrowed native typedef before binding analysis.
        /// </summary>
        /// <param name="settings">Active generation configuration.</param>
        /// <param name="cppTypedef">Mutable declaration valid only while the current compilation remains alive.</param>
        protected virtual void PatchTypedef(
            CsCodeGeneratorConfig settings,
            CppTypedef cppTypedef
        ) {
        }

        /// <summary>
        /// Transforms one borrowed native function before binding analysis.
        /// </summary>
        /// <param name="settings">Active generation configuration.</param>
        /// <param name="cppFunction">Mutable declaration valid only while the current compilation remains alive.</param>
        protected virtual void PatchFunction(
            CsCodeGeneratorConfig settings,
            CppFunction cppFunction
        ) {
        }

        /// <summary>
        /// Transforms one borrowed native enum before binding analysis.
        /// </summary>
        /// <param name="settings">Active generation configuration.</param>
        /// <param name="cppEnum">Mutable declaration valid only while the current compilation remains alive.</param>
        protected virtual void PatchEnum(
            CsCodeGeneratorConfig settings,
            CppEnum cppEnum
        ) {
        }
    }
}
