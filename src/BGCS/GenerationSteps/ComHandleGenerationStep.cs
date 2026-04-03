namespace BGCS.GenerationSteps
{
    public class ComHandleGenerationStep : HandleGenerationStep
    {
        public ComHandleGenerationStep(CsCodeGenerator generator, CsCodeGeneratorConfig config) : base(generator, config)
        {
        }

        protected override List<string> SetupHandleUsings()
        {
            var usings = base.SetupHandleUsings();
            usings.Add("BGCS.Runtime.COM");
            return usings;
        }
    }
}