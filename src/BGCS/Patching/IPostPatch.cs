namespace BGCS.Patching
{
    using System.Collections.Generic;
    using BGCS.Metadata;

    /// <summary>
    /// Transforms staged managed output before validation and atomic publication.
    /// </summary>
    public interface IPostPatch
    {

        /// <summary>
        /// Applies a transformation to generated sources before output validation and publication.
        /// </summary>
        /// <param name="context">Attempt-local patch state and controlled output rewrite operations.</param>
        /// <param name="metadata">Generated declaration metadata for this attempt.</param>
        /// <param name="files">Relative source paths within the context; original output paths must not be opened directly.</param>
        void Apply(
            PatchContext context,
            CsCodeGeneratorMetadata metadata,
            List<string> files
        );
    }
}
