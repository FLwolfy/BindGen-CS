namespace BGCS.GenerationSteps
{
    public class ComDelegateGenerationStep : DelegateGenerationStep
    {
        public ComDelegateGenerationStep(CsCodeGenerator generator, CsCodeGeneratorConfig config) : base(generator, config)
        {
        }

        protected override List<string> SetupDelegateUsings()
        {
            List<string> usings = base.SetupDelegateUsings();
            usings.Add("BGCS.Runtime.COM");
            return usings;
        }
    }
}