namespace BGCS.Configuration;

/// <summary>
/// Validates generator configuration before output is created or replaced.
/// </summary>
public sealed class ConfigValidator
{
    public static void Validate(CsCodeGeneratorConfig config)
    {
        CsCodeGeneratorConfigValidator.Validate(config);
    }
}
