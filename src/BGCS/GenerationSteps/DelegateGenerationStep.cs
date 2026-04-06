namespace BGCS.GenerationSteps
{
    using BGCS.Core;
    using BGCS.Core.Collections;
    using BGCS.Core.CSharp;
    using BGCS.CppAst.Model.Declarations;
    using BGCS.CppAst.Model.Interfaces;
    using BGCS.CppAst.Model.Types;
    using BGCS.Metadata;
    using System.IO;

    /// <summary>
    /// Defines the public class <c>DelegateGenerationStep</c>.
    /// </summary>
    public class DelegateGenerationStep : GenerationStep
    {
        protected readonly HashSet<CsDelegate> LibDefinedDelegates = new(IdentifierComparer<CsDelegate>.Default);
        /// <summary>
        /// Executes public operation <c>new</c>.
        /// </summary>
        public readonly HashSet<CsDelegate> DefinedDelegates = new(IdentifierComparer<CsDelegate>.Default);
        private readonly HashSet<string> csDelegateNames = [];

        /// <summary>
        /// Initializes a new instance of <see cref="DelegateGenerationStep"/>.
        /// </summary>
        public DelegateGenerationStep(CsCodeGenerator generator, CsCodeGeneratorConfig config) : base(generator, config)
        {
        }

        /// <summary>
        /// Gets <c>Name</c>.
        /// </summary>
        public override string Name { get; } = "Delegates";

        /// <summary>
        /// Executes public operation <c>Configure</c>.
        /// </summary>
        public override void Configure(CsCodeGeneratorConfig config)
        {
            Enabled = config.GenerateDelegates;
        }

        /// <summary>
        /// Executes public operation <c>CopyToMetadata</c>.
        /// </summary>
        public override void CopyToMetadata(CsCodeGeneratorMetadata metadata)
        {
            metadata.DefinedDelegates.AddRange(DefinedDelegates);
        }

        /// <summary>
        /// Executes public operation <c>CopyFromMetadata</c>.
        /// </summary>
        public override void CopyFromMetadata(CsCodeGeneratorMetadata metadata)
        {
            LibDefinedDelegates.AddRange(metadata.DefinedDelegates);
        }

        /// <summary>
        /// Executes public operation <c>Reset</c>.
        /// </summary>
        public override void Reset()
        {
            LibDefinedDelegates.Clear();
            DefinedDelegates.Clear();
            csDelegateNames.Clear();
        }

        protected virtual List<string> SetupDelegateUsings()
        {
            List<string> usings =
            [
                "System", "System.Diagnostics", "System.Runtime.CompilerServices", "System.Runtime.InteropServices", "BGCS.Runtime",
                .. config.Usings,
            ];
            return usings;
        }

        protected virtual bool FilterIgnoredType(GenContext context, CppClass cppClass)
        {
            if (config.AllowedTypes.Count != 0 && !config.AllowedTypes.Contains(cppClass.Name))
                return true;

            if (config.IgnoredTypes.Contains(cppClass.Name))
                return true;

            return false;
        }

        protected virtual bool FilterDelegate(GenContext context, CsDelegate csDelegate)
        {
            if (config.AllowedDelegates.Count != 0 && !config.AllowedDelegates.Contains(csDelegate.CppName))
                return true;
            if (config.IgnoredDelegates.Contains(csDelegate.CppName))
                return true;

            if (LibDefinedDelegates.Contains(csDelegate))
                return true;

            if (DefinedDelegates.Contains(csDelegate))
            {
                LogWarn($"{context.FilePath}: {csDelegate.Name} delegate is already defined!");
                return true;
            }

            DefinedDelegates.Add(csDelegate);

            return false;
        }

        /// <summary>
        /// Runs generation logic through <c>Generate</c>.
        /// </summary>
        public override void Generate(FileSet files, ParseResult result, string outputPath, CsCodeGeneratorConfig config, CsCodeGeneratorMetadata metadata)
        {
            var compilation = result.Compilation;
            string folder = Path.Combine(outputPath, "Delegates");
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
            Directory.CreateDirectory(folder);
            string filePath = Path.Combine(folder, "Delegates.cs");

            // Generate Delegates
            using var writer = new CsSplitCodeWriter(filePath, config.Namespace, SetupDelegateUsings(), config.HeaderInjector, 1);

            GenContext context = new(result, filePath, writer);

            for (int i = 0; i < compilation.Classes.Count; i++)
            {
                CppClass cppClass = compilation.Classes[i];

                if (!files.Contains(cppClass.SourceFile))
                    continue;

                if (!files.Contains(cppClass.SourceFile))
                    continue;

                if (FilterIgnoredType(context, cppClass))
                    continue;

                WriteClassDelegates(context, cppClass);

                writer.TrySplit();
            }

            for (int i = 0; i < compilation.Typedefs.Count; i++)
            {
                CppTypedef typedef = compilation.Typedefs[i];

                if (!files.Contains(typedef.SourceFile))
                    continue;

                if (typedef.ElementType is CppPointerType pointerType && pointerType.ElementType is CppFunctionType functionType)
                {
                    WriteDelegate(context, typedef, functionType);
                }

                writer.TrySplit();
            }
        }

        protected virtual void WriteClassDelegates(GenContext context, CppClass cppClass, string? csName = null)
        {
            csName ??= config.GetDelegateName(cppClass.Name);

            if (cppClass.ClassKind == CppClassKind.Class || cppClass.Name.EndsWith("_T") || csName == "void")
            {
                return;
            }

            for (int j = 0; j < cppClass.Classes.Count; j++)
            {
                var subClass = cppClass.Classes[j];
                string csSubName;
                if (string.IsNullOrEmpty(subClass.Name))
                {
                    string label = cppClass.Classes.Count == 1 ? "" : j.ToString();
                    csSubName = csName + "Union" + label;
                }
                else
                {
                    csSubName = config.GetDelegateName(subClass.Name);
                }

                WriteClassDelegates(context, subClass, csSubName);
            }

            for (int j = 0; j < cppClass.Fields.Count; j++)
            {
                CppField cppField = cppClass.Fields[j];

                if (cppField.Type is CppPointerType cppPointer && cppPointer.IsDelegate(out var functionType))
                {
                    WriteDelegate(context, cppField, functionType);
                }
                else if (cppField.Type is CppTypedef typedef && typedef.ElementType is CppPointerType pointerType && pointerType.ElementType is CppFunctionType cppFunctionType)
                {
                    WriteDelegate(context, cppField, cppFunctionType);
                }
            }
        }

        private void WriteDelegate<T>(GenContext context, T field, CppFunctionType functionType) where T : class, ICppDeclaration, ICppMember
        {
            string csDelegateName = config.GetDelegateName(field.Name);

            int i = 1;
            string name = csDelegateName;
            while (csDelegateNames.Contains(name))
            {
                name = $"{csDelegateName}{i++}";
            }
            csDelegateName = name;
            csDelegateNames.Add(name);

            CsDelegate csDelegate = generator.CreateCsDelegate(field, csDelegateName, functionType);

            if (FilterDelegate(context, csDelegate))
            {
                return;
            }

            var writer = context.Writer;

            WriteFinal(writer, field, functionType, csDelegateName);
            writer.WriteLine();
        }

        private void WriteFinal<T>(ICodeWriter writer, T field, CppFunctionType functionType, string csFieldName, bool compatibility = false) where T : class, ICppDeclaration, ICppMember
        {
            string signature = config.GetParameterSignature(functionType.Parameters, canUseOut: false, delegateType: true, compatibility: compatibility);
            string returnCsName = config.GetCsTypeName(functionType.ReturnType);
            returnCsName = returnCsName.Replace("bool", config.GetBoolType());

            if (functionType.ReturnType is CppTypedef typedef && typedef.ElementType.IsDelegate(out var cppFunction) && !returnCsName.Contains('*'))
            {
                if (cppFunction.Parameters.Count == 0)
                {
                    returnCsName = $"delegate*<{config.GetCsTypeName(cppFunction.ReturnType)}>";
                }
                else
                {
                    returnCsName = $"delegate*<{config.GetNamelessParameterSignature(cppFunction.Parameters, canUseOut: false, delegateType: true, compatibility)}, {config.GetCsTypeName(cppFunction.ReturnType)}>";
                }
            }

            if (compatibility && returnCsName.Contains('*'))
            {
                returnCsName = "nint";
            }

            if (config.TryGetDelegateMapping(csFieldName, out var mapping))
            {
                returnCsName = mapping.ReturnType;
                signature = mapping.Signature;
            }

            string header = $"{returnCsName} {csFieldName}({signature})";

            config.WriteCsSummary(field.Comment, writer);
            if (config.GenerateMetadata)
            {
                writer.WriteLine($"[NativeName(NativeNameType.Delegate, \"{field.Name}\")]");
            }
            writer.WriteLine($"[UnmanagedFunctionPointer(CallingConvention.{functionType.CallingConvention.GetCallingConvention()})]");
            writer.WriteLine($"public unsafe delegate {header};");
            writer.WriteLine();
        }
    }
}
