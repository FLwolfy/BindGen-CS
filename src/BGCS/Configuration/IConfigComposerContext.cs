namespace BGCS.Configuration;

internal interface IConfigComposerContext
{
    void Compose(ref CsCodeGeneratorConfig config, string baseDirectory);
}
