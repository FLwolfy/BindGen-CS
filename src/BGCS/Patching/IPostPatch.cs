namespace BGCS.Patching
{
    using BGCS.Metadata;
    using System.Collections.Generic;

    /// <summary>
    /// Defines the public interface <c>IPostPatch</c>.
    /// </summary>
    public interface IPostPatch : IPatch
    {
        void Apply(PatchContext context, CsCodeGeneratorMetadata metadata, List<string> files);
    }
}
