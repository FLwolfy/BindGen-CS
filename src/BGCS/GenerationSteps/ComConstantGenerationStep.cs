namespace BGCS.GenerationSteps
{
    using System.Collections.Generic;

    public class ComConstantGenerationStep : ConstantGenerationStep
    {
        public ComConstantGenerationStep(CsCodeGenerator generator, CsCodeGeneratorConfig config) : base(generator, config)
        {
        }

        protected override List<string> SetupConstantUsings()
        {
            var usings = base.SetupConstantUsings();
            usings.Add("BGCS.Runtime.COM");
            return usings;
        }
    }
}