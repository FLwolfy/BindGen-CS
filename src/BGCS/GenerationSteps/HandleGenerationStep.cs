namespace BGCS.GenerationSteps
{
    using BGCS.Core;
    using BGCS.Core.Collections;
    using BGCS.CppAst.Model.Declarations;
    using BGCS.CppAst.Model.Types;
    using BGCS.Metadata;
    using System.Collections.Generic;
    using System.IO;

    /// <summary>
    /// Defines the public class <c>HandleGenerationStep</c>.
    /// </summary>
    public class HandleGenerationStep : GenerationStep
    {
        protected readonly HashSet<string> LibDefinedTypedefs = new();
        /// <summary>
        /// Executes public operation <c>new</c>.
        /// </summary>
        public readonly HashSet<string> DefinedTypedefs = new();

        /// <summary>
        /// Initializes a new instance of <see cref="HandleGenerationStep"/>.
        /// </summary>
        public HandleGenerationStep(CsCodeGenerator generator, CsCodeGeneratorConfig config) : base(generator, config)
        {
        }

        /// <summary>
        /// Gets <c>Name</c>.
        /// </summary>
        public override string Name { get; } = "Handles";

        /// <summary>
        /// Executes public operation <c>Configure</c>.
        /// </summary>
        public override void Configure(CsCodeGeneratorConfig config)
        {
            Enabled = config.GenerateHandles;
        }

        /// <summary>
        /// Executes public operation <c>CopyToMetadata</c>.
        /// </summary>
        public override void CopyToMetadata(CsCodeGeneratorMetadata metadata)
        {
            metadata.DefinedTypedefs.AddRange(DefinedTypedefs);
        }

        /// <summary>
        /// Executes public operation <c>CopyFromMetadata</c>.
        /// </summary>
        public override void CopyFromMetadata(CsCodeGeneratorMetadata metadata)
        {
            LibDefinedTypedefs.AddRange(metadata.DefinedTypedefs);
        }

        /// <summary>
        /// Executes public operation <c>Reset</c>.
        /// </summary>
        public override void Reset()
        {
            LibDefinedTypedefs.Clear();
            DefinedTypedefs.Clear();
        }

        protected virtual List<string> SetupHandleUsings()
        {
            List<string> usings = ["System", "System.Diagnostics", "System.Runtime.InteropServices", "BGCS.Runtime", .. config.Usings];
            return usings;
        }

        protected virtual bool FilterHandle(GenContext? context, CsHandleMetadata csHandle)
        {
            var typedef = csHandle.CppType;
            if (config.AllowedTypedefs.Count != 0 && !config.AllowedTypedefs.Contains(typedef.Name))
                return true;

            if (config.IgnoredTypedefs.Contains(typedef.Name))
                return true;

            if (LibDefinedTypedefs.Contains(typedef.Name))
                return true;

            if (DefinedTypedefs.Contains(csHandle.Name))
            {
                LogWarn($"{context?.FilePath}: {typedef} is already defined!");
                return true;
            }

            DefinedTypedefs.Add(csHandle.Name);

            if (typedef.ElementType is CppPointerType pointerType && pointerType.ElementType is not CppFunctionType)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Runs generation logic through <c>Generate</c>.
        /// </summary>
        public override void Generate(FileSet files, ParseResult result, string outputPath, CsCodeGeneratorConfig config, CsCodeGeneratorMetadata metadata)
        {
            var compilation = result.Compilation;
            string folder = Path.Combine(outputPath, "Handles");
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
            Directory.CreateDirectory(folder);

            if (config.OneFilePerType)
            {
                for (int i = 0; i < compilation.Typedefs.Count; i++)
                {
                    var typedef = compilation.Typedefs[i];
                    if (!files.Contains(typedef.SourceFile))
                        continue;

                    var handle = ParseHandle(typedef);
                    if (FilterHandle(null, handle))
                        continue;

                    string filePath = Path.Combine(folder, $"{handle.Name}.cs");
                    using var writer = new CsCodeWriter(filePath, config.Namespace, SetupHandleUsings(), config.HeaderInjector);
                    GenContext context = new(result, filePath, writer);
                    WriteHandle(context, handle);
                }
            }
            else
            {
                string filePath = Path.Combine(folder, "Handles.cs");
                using var writer = new CsSplitCodeWriter(filePath, config.Namespace, SetupHandleUsings(), config.HeaderInjector, 1);
                GenContext context = new(result, filePath, writer);

                for (int i = 0; i < compilation.Typedefs.Count; i++)
                {
                    var typedef = compilation.Typedefs[i];
                    if (!files.Contains(typedef.SourceFile))
                        continue;

                    var handle = ParseHandle(typedef);
                    if (FilterHandle(context, handle))
                        continue;
                    WriteHandle(context, handle);
                    if (i + 1 != compilation.Typedefs.Count)
                    {
                        writer.WriteLine();
                    }
                }
            }
        }

        protected virtual CsHandleMetadata ParseHandle(CppTypedef typedef)
        {
            var csName = config.GetCsCleanName(typedef.Name);
            CsHandleMetadata metadata = new(csName, typedef, null, true);

            config.TryGetHandleMapping(typedef.Name, out var mapping);

            metadata.Comment = config.WriteCsSummary(typedef.Comment);
            if (mapping != null)
            {
                if (mapping.Comment != null)
                {
                    metadata.Comment = config.WriteCsSummary(mapping.Comment);
                }
                if (mapping.FriendlyName != null)
                {
                    metadata.Name = mapping.FriendlyName;
                }
            }

            return metadata;
        }

        protected virtual void WriteHandle(GenContext context, CsHandleMetadata csHandle)
        {
            var writer = context.Writer;

            LogInfo("defined handle " + (string?)csHandle.Name);

            writer.WriteLines(csHandle.Comment);
            if (config.GenerateMetadata)
            {
                writer.WriteLine($"[NativeName(NativeNameType.Typedef, \"{csHandle.CppType.Name}\")]");
            }
            writer.WriteLine($"[DebuggerDisplay(\"{{DebuggerDisplay,nq}}\")]");
            using (writer.PushBlock($"public readonly partial struct {csHandle.Name} : IEquatable<{csHandle.Name}>"))
            {
                string handleType = csHandle.IsDispatchable ? "nint" : "ulong";
                string nullValue = "0";

                writer.WriteLine($"public {csHandle.Name}({handleType} handle) {{ Handle = handle; }}");
                writer.WriteLine($"public {handleType} Handle {{ get; }}");
                writer.WriteLine($"public bool IsNull => Handle == 0;");

                writer.WriteLine($"public static {csHandle.Name} Null => new {csHandle.Name}({nullValue});");
                writer.WriteLine($"public static implicit operator {csHandle.Name}({handleType} handle) => new {csHandle.Name}(handle);");
                writer.WriteLine($"public static bool operator ==({csHandle.Name} left, {csHandle.Name} right) => left.Handle == right.Handle;");
                writer.WriteLine($"public static bool operator !=({csHandle.Name} left, {csHandle.Name} right) => left.Handle != right.Handle;");
                writer.WriteLine($"public static bool operator ==({csHandle.Name} left, {handleType} right) => left.Handle == right;");
                writer.WriteLine($"public static bool operator !=({csHandle.Name} left, {handleType} right) => left.Handle != right;");
                writer.WriteLine($"public bool Equals({csHandle.Name} other) => Handle == other.Handle;");
                writer.WriteLine("/// <inheritdoc/>");
                writer.WriteLine($"public override bool Equals(object obj) => obj is {csHandle.Name} handle && Equals(handle);");
                writer.WriteLine("/// <inheritdoc/>");
                writer.WriteLine($"public override int GetHashCode() => Handle.GetHashCode();");
                writer.WriteLine($"private string DebuggerDisplay => string.Format(\"{csHandle.Name} [0x{{0}}]\", Handle.ToString(\"X\"));");
            }
        }
    }
}
