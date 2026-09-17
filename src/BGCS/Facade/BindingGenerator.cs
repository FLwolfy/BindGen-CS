namespace BGCS.Facade;

using BGCS.Intermediate;

/// <summary>
/// Provides the concise configuration-file facade for new applications while preserving <see cref="CsCodeGenerator"/>.
/// </summary>
public static class BindingGenerator
{
    public static BindingGenerationResult Generate(string configPath, string? outputPath = null)
    {
        CsCodeGenerator generator = CsCodeGenerator.Create(configPath);
        bool success = generator.GenerateConfigured(outputPath);
        return generator.LastResult ?? new(null, success, [], generator.Messages.Select(diagnostic =>
            new BindingDiagnostic((BindingDiagnosticSeverity)(int)diagnostic.Severtiy, diagnostic.Message)).ToArray());
    }
}
