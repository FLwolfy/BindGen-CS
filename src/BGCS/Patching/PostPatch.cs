namespace BGCS.Patching
{
    using System;
    using System.Collections.Generic;
    using BGCS.Metadata;

    /// <summary>
    /// Applies an ordered catalog of text transformations to selected candidate files.
    /// </summary>
    public abstract class PostPatch : IPostPatch
    {
        private readonly List<RegexPatch> m_regexPatches = [];
        /// <summary>
        /// Appends a regular-expression transformation after the existing text transformations.
        /// </summary>
        /// <param name="patch">The transformation retained for this patch's lifetime.</param>
        /// <exception cref="ArgumentNullException">The transformation is null.</exception>
        public void AddRegexPatch(RegexPatch patch)
        {
            ArgumentNullException.ThrowIfNull(patch);
            this.m_regexPatches.Add(patch);
        }

        /// <summary>
        /// Rewrites selected files within the caller's candidate output transaction.
        /// </summary>
        /// <param name="context">The candidate read/write boundary borrowed for this stage.</param>
        /// <param name="metadata">Mutable generation metadata; text transformations do not change it.</param>
        /// <param name="files">Relative selected candidate file names in transformation order.</param>
        public virtual void Apply(
            PatchContext context,
            CsCodeGeneratorMetadata metadata,
            List<string> files
        ) {
            PatchFiles(context, files);
        }

        /// <summary>
        /// Applies registered rewrites through the controlled staged output context.
        /// </summary>
        /// <param name="context">Context that reads and writes candidate output files.</param>
        /// <param name="files">Candidate generated source files.</param>
        protected virtual void PatchFiles(
            PatchContext context,
            List<string> files
        ) {
            foreach (var file in files)
            {
                PatchFile(context, file);
            }
        }

        /// <summary>
        /// Applies registered text transformations to one staged generated source.
        /// </summary>
        /// <param name="context">Context that reads and writes candidate output.</param>
        /// <param name="file">Candidate source path.</param>
        protected virtual void PatchFile(
            PatchContext context,
            string file
        ) {
            var text = context.ReadFile(file);
            foreach (var patch in this.m_regexPatches)
            {
                patch.PostPatch(file, ref text);
            }

            context.WriteFile(file, text);
        }
    }
}
