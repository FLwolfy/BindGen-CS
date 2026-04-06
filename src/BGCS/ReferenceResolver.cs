namespace BGCS
{
    using BGCS.Core;
    using BGCS.CppAst.Model.Metadata;
    using BGCS.CppAst.Model.Types;

    /// <summary>
    /// Defines the public class <c>ReferenceResolver</c>.
    /// </summary>
    public class ReferenceResolver
    {
        /// <summary>
        /// Executes public operation <c>Prefilter</c>.
        /// </summary>
        public virtual void Prefilter(FileSet set, CppCompilation compilation, CsCodeGeneratorConfig config)
        {
            CppCompilation outputCompilation = new(compilation.TranslationUnit);

            HashSet<string> typedefs = [];
            foreach (var typedef in compilation.Typedefs)
            {
                if (set.Contains(typedef.SourceFile))
                {
                    typedefs.Add(typedef.FullName);
                    outputCompilation.Typedefs.Add(typedef);
                    Resolve(typedef, compilation, outputCompilation);
                }
            }

            foreach (var cppEnum in compilation.Enums)
            {
                if (set.Contains(cppEnum.SourceFile))
                {
                    outputCompilation.Enums.Add(cppEnum);
                }
            }

            foreach (var function in compilation.Functions)
            {
                if (set.Contains(function.SourceFile))
                {
                    outputCompilation.Functions.Add(function);
                }
            }

            foreach (var cppClass in compilation.Classes)
            {
                if (set.Contains(cppClass.SourceFile))
                {
                    outputCompilation.Classes.Add(cppClass);
                }
            }

            foreach (var field in compilation.Fields)
            {
                if (set.Contains(field.SourceFile))
                {
                    outputCompilation.Fields.Add(field);
                }
            }

            foreach (var macro in compilation.Macros)
            {
                if (set.Contains(macro.SourceFile))
                {
                    outputCompilation.Macros.Add(macro);
                }
            }
        }

        /// <summary>
        /// Executes public operation <c>Resolve</c>.
        /// </summary>
        public virtual void Resolve(CppType cppType, CppCompilation input, CppCompilation output)
        {
            if (cppType.IsEnum(out var cppEnum))
            {
                output.Enums.Add(cppEnum);
            }
            if (cppType.IsDelegate(out var cppFunction))
            {
                output.Enums.Add(cppEnum);
            }
        }
    }
}
