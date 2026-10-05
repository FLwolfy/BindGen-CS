using System.Linq;
using BGCS.Configuration;
using BGCS.Conversion;
using BGCS.Core.IO;
using BGCS.Facade;

namespace BGCS.Analysis.PreProcessing
{
    using System.Collections.Frozen;
    using BGCS.CppAst.Model;
    using BGCS.CppAst.Model.Metadata;
    using BGCS.CSharp;
    using BGCS.Metadata;

    /// <summary>
    /// Recognizes selected object-like macros that alias existing free-function exports.
    /// </summary>
    public class ConstantPreProcessStep : PreProcessStep
    {
        /// <summary>
        /// Creates the function-alias preprocessing stage for one generator.
        /// </summary>
        /// <param name="generator">
        /// The owner receiving preprocessing diagnostics.
        /// </param>
        /// <param name="config">
        /// The generation naming policy borrowed by this stage.
        /// </param>
        public ConstantPreProcessStep(
            CsCodeGenerator generator,
            CsCodeGeneratorConfig config
        ) : base(generator, config)
        {
        }

        /// <summary>
        /// Adds function aliases from selected macros without rewriting headers or native declarations.
        /// </summary>
        /// <param name="files">
        /// The selected macro source files.
        /// </param>
        /// <param name="compilation">
        /// The borrowed compilation supplying free-function names and macros.
        /// </param>
        /// <param name="config">
        /// The active policy; alias normalization uses the policy retained by this stage.
        /// </param>
        /// <param name="metadata">
        /// Generated metadata, unchanged by this stage.
        /// </param>
        /// <param name="result">
        /// The attempt-owned alias catalog receiving recognized mappings.
        /// </param>
        public override void PreProcess(
            FileSet files,
            CppCompilation compilation,
            CsCodeGeneratorConfig config,
            CsCodeGeneratorMetadata metadata,
            ParseResult result
        ) {
            FrozenSet<string> functionNames = compilation.functions.Select(f => f.name).ToFrozenSet();
            foreach (CppMacro macro in compilation.macros)
            {
                if (!files.Contains(macro.sourceFile))
                    continue;
                ProcessConstant(macro, functionNames, result);
            }
        }

        private void ProcessConstant(
            CppMacro macro,
            FrozenSet<string> functionNames,
            ParseResult result
        ) {
            var value = macro.value.NormalizeConstantValue();
            if (functionNames.Contains(value))
            {
                string name = config.GetCsFunctionName(macro.name);
                FunctionAlias alias = new(value, macro.name, name, null);
                result.AddFunctionAlias(alias);
            }
        }
    }
}
