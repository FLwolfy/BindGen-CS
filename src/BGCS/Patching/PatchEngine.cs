namespace BGCS.Patching
{
    using BGCS.Metadata;
    using Newtonsoft.Json;
    using System.Collections.Generic;

    /// <summary>
    /// Defines the public class <c>PatchEngine</c>.
    /// </summary>
    public class PatchEngine
    {
        private readonly List<IPrePatch> preGenerationPatches = new();
        private readonly List<IPostPatch> postGenerationPatches = new();
        private readonly string baseStagePath;

        /// <summary>
        /// Initializes a new instance of <see cref="PatchEngine"/>.
        /// </summary>
        public PatchEngine(string baseStagePath)
        {
            this.baseStagePath = baseStagePath;
        }

        /// <summary>
        /// Executes public operation <c>PatchEngine</c>.
        /// </summary>
        public PatchEngine() : this($"patches/{Guid.NewGuid()}")
        {
        }

        /// <summary>
        /// Executes public operation <c>RegisterPrePatch</c>.
        /// </summary>
        public void RegisterPrePatch(IPrePatch patch)
        {
            preGenerationPatches.Add(patch);
        }

        /// <summary>
        /// Executes public operation <c>RegisterPostPatch</c>.
        /// </summary>
        public void RegisterPostPatch(IPostPatch patch)
        {
            postGenerationPatches.Add(patch);
        }

        internal void Build()
        {
        }

        private readonly JsonSerializerSettings options = new() { Formatting = Formatting.Indented };

        /// <summary>
        /// Executes public operation <c>ApplyPrePatches</c>.
        /// </summary>
        public void ApplyPrePatches(CsCodeGeneratorConfig settings, string outputDir, List<string> files, ParseResult result)
        {
            PatchContext? last = null;
            for (int i = 0; i < preGenerationPatches.Count; i++)
            {
                IPrePatch? patch = preGenerationPatches[i];
                PatchContext context = new(Path.Combine(baseStagePath, "pre", $"stage{i}"));
                if (last != null)
                {
                    context.CopyFromStage(last);
                }
                else
                {
                    context.CopyFromInput(outputDir, files);
                }

                patch.Apply(context, settings, files, result);
                context.WriteFile("settings.json", JsonConvert.SerializeObject(settings, options));
                last = context;
            }

            last?.CopyToOutput(outputDir);
        }

        /// <summary>
        /// Executes public operation <c>ApplyPostPatches</c>.
        /// </summary>
        public void ApplyPostPatches(CsCodeGeneratorMetadata metadata, string outputDir, List<string> files)
        {
            PatchContext? last = null;
            for (int i = 0; i < postGenerationPatches.Count; i++)
            {
                IPostPatch? patch = postGenerationPatches[i];
                PatchContext context = new(Path.Combine(baseStagePath, "post", $"stage{i}"));
                if (last != null)
                {
                    context.CopyFromStage(last);
                }
                else
                {
                    context.CopyFromInput(outputDir, files);
                }
                patch.Apply(context, metadata, files);
                last = context;
            }

            last?.CopyToOutput(outputDir);
        }
    }
}
