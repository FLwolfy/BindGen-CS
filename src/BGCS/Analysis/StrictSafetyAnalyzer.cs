namespace BGCS.Analysis;

using BGCS.Intermediate;

/// <summary>
/// Reports native semantics that cannot be proven from declarations and require minimal explicit configuration.
/// </summary>
public sealed class StrictSafetyAnalyzer
{
    private readonly CsCodeGeneratorConfig config;

    public StrictSafetyAnalyzer(CsCodeGeneratorConfig config)
    {
        this.config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public void Analyze(BindingModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        config.UnsafeFriendlyFunctions.Clear();
        if (!config.StrictSafety)
            return;
        foreach (BindingFunction function in module.Functions)
        {
            config.MarshallingMappings.TryGetValue(function.NativeName, out FunctionMarshallingMapping? mapping);
            if (function.ReturnType.PointerDepth > 0 && mapping?.Return?.Ownership == null)
                Add(module, "BGCS-SAFETY-OWNERSHIP", function,
                    "pointer return ownership is not declared",
                    $"MarshallingMappings.{function.NativeName}.Return.Ownership");
            for (int i = 0; i < function.Parameters.Count; i++)
            {
                BindingParameter parameter = function.Parameters[i];
                MarshallingMapping? parameterMapping = null;
                mapping?.Parameters.TryGetValue(parameter.NativeName, out parameterMapping);
                if (parameter.Marshalling.Strategy == MarshallingStrategy.Callback && parameterMapping == null)
                    Add(module, "BGCS-SAFETY-CALLBACK", function,
                        $"callback parameter '{parameter.NativeName}' retention/unregister lifetime is not declared",
                        $"MarshallingMappings.{function.NativeName}.Parameters.{parameter.NativeName}");
                if (LooksLikeBuffer(parameter) && parameter.Marshalling.LengthParameter == null &&
                    parameter.Marshalling.CapacityParameter == null && parameterMapping == null)
                    Add(module, "BGCS-SAFETY-LENGTH", function,
                        $"buffer parameter '{parameter.NativeName}' has no proven length or capacity relationship",
                        $"MarshallingMappings.{function.NativeName}.Parameters.{parameter.NativeName}.LengthParameter");
                if (parameter.Marshalling.Strategy == MarshallingStrategy.String &&
                    parameter.Direction != BindingDirection.In && parameter.Marshalling.CleanupFunction == null && parameterMapping == null)
                    Add(module, "BGCS-SAFETY-ALLOCATOR", function,
                        $"output string parameter '{parameter.NativeName}' has no cleanup allocator",
                        $"MarshallingMappings.{function.NativeName}.Parameters.{parameter.NativeName}.CleanupFunction");
            }
        }
    }

    private static bool LooksLikeBuffer(BindingParameter parameter)
    {
        if (parameter.Type.PointerDepth == 0)
            return false;
        string name = parameter.NativeName;
        return name.Contains("buffer", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("data", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("items", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("values", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("output", StringComparison.OrdinalIgnoreCase);
    }

    private void Add(BindingModule module, string code, BindingFunction function, string reason, string configuration)
    {
        if (config.StrictSafetySeverity is StrictSafetySeverity.SuppressFriendly or StrictSafetySeverity.Error)
            config.UnsafeFriendlyFunctions.Add(function.NativeName);
        BindingDiagnosticSeverity severity = config.StrictSafetySeverity == StrictSafetySeverity.Error
            ? BindingDiagnosticSeverity.Error
            : BindingDiagnosticSeverity.Warning;
        module.StructuredDiagnostics.Add(new(severity,
            $"{function.NativeName}: {reason}. Add the minimum explicit setting '{configuration}'. Raw ABI remains available; do not infer a friendly ownership API until configured.", code));
    }
}
