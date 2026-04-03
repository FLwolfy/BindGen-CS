namespace BGCS.Patching
{
    using BGCS.Metadata;
    using System.Collections.Generic;

    public interface IPostPatch : IPatch
    {
        void Apply(PatchContext context, CsCodeGeneratorMetadata metadata, List<string> files);
    }
}