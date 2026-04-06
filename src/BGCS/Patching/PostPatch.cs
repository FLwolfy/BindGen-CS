namespace BGCS.Patching
{
    using BGCS.Metadata;
    using System.Collections.Generic;

    /// <summary>
    /// Defines the public class <c>PostPatch</c>.
    /// </summary>
    public abstract class PostPatch : IPostPatch
    {
        private readonly List<RegexPatch> regexPatches = [];

        /// <summary>
        /// Adds data or behavior through <c>AddRegexPatch</c>.
        /// </summary>
        public void AddRegexPatch(RegexPatch patch)
        {
            regexPatches.Add(patch);
        }

        /// <summary>
        /// Executes public operation <c>Apply</c>.
        /// </summary>
        public virtual void Apply(PatchContext context, CsCodeGeneratorMetadata metadata, List<string> files)
        {
            PatchFiles(context, files);
        }

        protected virtual void PatchFiles(PatchContext context, List<string> files)
        {
            foreach (var file in files)
            {
                PatchFile(context, file);
            }
        }

        protected virtual void PatchFile(PatchContext context, string file)
        {
            var text = context.ReadFile(file);

            foreach (var patch in regexPatches)
            {
                patch.PostPatch(file, ref text);
            }

            context.WriteFile(file, text);
        }
    }
}
