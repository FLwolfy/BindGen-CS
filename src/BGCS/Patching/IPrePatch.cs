namespace BGCS.Patching
{
    using System.Collections.Generic;

    /// <summary>
    /// Defines the public interface <c>IPrePatch</c>.
    /// </summary>
    public interface IPrePatch : IPatch
    {
        void Apply(PatchContext context, CsCodeGeneratorConfig settings, List<string> files, ParseResult compilation);
    }
}
