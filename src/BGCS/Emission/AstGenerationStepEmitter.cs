namespace BGCS.Emission;

using BGCS.Core;
using BGCS.GenerationSteps;
using BGCS.Metadata;

/// <summary>
/// Isolates the compatibility AST-to-C# generation steps while their semantics are lowered into Binding IR.
/// This is intentionally not an <see cref="IBindingEmitter"/> because it consumes parser state.
/// </summary>
internal static class AstGenerationStepEmitter
{
    internal static void Emit(CsCodeGenerator generator, FileSet files, ParseResult result, string outputPath,
        CsCodeGeneratorConfig config, CsCodeGeneratorMetadata metadata, bool explicitEmptyOutputFilter)
    {
        generator.LogInfo("Configuring compatibility C# lowering steps...");
        foreach (GenerationStep step in generator.GenerationSteps)
            step.Configure(config);
        if (explicitEmptyOutputFilter)
            return;
        foreach (GenerationStep step in generator.GenerationSteps.Where(step => step.Enabled))
        {
            generator.LogInfo($"Generating {step.Name}...");
            step.Generate(files, result, outputPath, config, metadata);
            step.CopyToMetadata(metadata);
        }
    }
}
