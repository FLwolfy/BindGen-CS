namespace BGCS
{
    public interface IConfigComposer
    {
        void Compose(ref CsCodeGeneratorConfig config);
    }
}