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
        if (!config.StrictSafety)
            return;
        foreach (BindingFunction function in module.Functions)
        {
            config.MarshallingMappings.TryGetValue(function.NativeName, out FunctionMarshallingMapping? mapping);
            if (function.ReturnType.PointerDepth > 0 && mapping?.Return?.Ownership == null)
                Add(module, BindingDiagnosticCodes.Ownership, function,
                    "pointer return ownership is not declared",
                    $"MarshallingMappings.{function.NativeName}.Return.Ownership");
            for (int i = 0; i < function.Parameters.Count; i++)
            {
                BindingParameter parameter = function.Parameters[i];
                MarshallingMapping? parameterMapping = null;
                mapping?.Parameters.TryGetValue(parameter.NativeName, out parameterMapping);
                if (parameter.Marshalling.Strategy == MarshallingStrategy.Callback)
                {
                    if (parameter.Marshalling.CallbackLifetime == BindingCallbackLifetime.Unspecified)
                        Add(module, BindingDiagnosticCodes.CallbackLifetime, function,
                            $"callback parameter '{parameter.NativeName}' retention lifetime is not declared",
                            $"MarshallingMappings.{function.NativeName}.Parameters.{parameter.NativeName}.CallbackLifetime");
                    if (parameter.Marshalling.CallbackThreading == BindingCallbackThreading.Unspecified)
                        Add(module, BindingDiagnosticCodes.CallbackThreading, function,
                            $"callback parameter '{parameter.NativeName}' invocation threading is not declared",
                            $"MarshallingMappings.{function.NativeName}.Parameters.{parameter.NativeName}.CallbackThreading");
                    if (parameter.Marshalling.CallbackLifetime == BindingCallbackLifetime.RetainedUntilUnregister &&
                        string.IsNullOrWhiteSpace(parameter.Marshalling.UnregisterFunction))
                        Add(module, BindingDiagnosticCodes.CallbackLifetime, function,
                            $"retained callback parameter '{parameter.NativeName}' has no synchronous unregister function",
                            $"MarshallingMappings.{function.NativeName}.Parameters.{parameter.NativeName}.UnregisterFunction");
                    if (parameter.Marshalling.CallbackLifetime == BindingCallbackLifetime.RetainedUntilCompletion &&
                        parameter.Marshalling.AsyncCompletion == BindingAsyncCompletion.None)
                        Add(module, BindingDiagnosticCodes.AsyncLifetime, function,
                            $"asynchronously retained callback parameter '{parameter.NativeName}' has no completion mechanism",
                            $"MarshallingMappings.{function.NativeName}.Parameters.{parameter.NativeName}.AsyncCompletion");
                    if (parameter.Marshalling.AsyncCompletion != BindingAsyncCompletion.None &&
                        string.IsNullOrWhiteSpace(parameter.Marshalling.CompletionFunction))
                        Add(module, BindingDiagnosticCodes.AsyncLifetime, function,
                            $"asynchronous callback parameter '{parameter.NativeName}' has no terminal completion function",
                            $"MarshallingMappings.{function.NativeName}.Parameters.{parameter.NativeName}.CompletionFunction");
                }
                if (LooksLikeBuffer(parameter) && parameter.Marshalling.LengthParameter == null &&
                    parameter.Marshalling.CapacityParameter == null && parameterMapping == null)
                    Add(module, BindingDiagnosticCodes.BufferLength, function,
                        $"buffer parameter '{parameter.NativeName}' has no proven length or capacity relationship",
                        $"MarshallingMappings.{function.NativeName}.Parameters.{parameter.NativeName}.LengthParameter");
                if (parameter.Marshalling.Strategy == MarshallingStrategy.String &&
                    parameter.Direction != BindingDirection.In && parameter.Marshalling.CleanupFunction == null && parameterMapping == null)
                    Add(module, BindingDiagnosticCodes.Allocator, function,
                        $"output string parameter '{parameter.NativeName}' has no cleanup allocator",
                        $"MarshallingMappings.{function.NativeName}.Parameters.{parameter.NativeName}.CleanupFunction");
                ValidateOwnedAllocator(module, function, parameter.NativeName, parameter.Marshalling,
                    $"MarshallingMappings.{function.NativeName}.Parameters.{parameter.NativeName}");
            }
            ValidateOwnedAllocator(module, function, "return value", function.ReturnMarshalling,
                $"MarshallingMappings.{function.NativeName}.Return");
        }
    }

    private void ValidateOwnedAllocator(BindingModule module, BindingFunction function, string valueName,
        MarshallingPlan plan, string configuration)
    {
        if (plan.Ownership != BindingOwnership.Owned && !plan.RequiresCleanup)
            return;
        if (plan.AllocatorKind == BindingAllocatorKind.Unspecified)
            Add(module, BindingDiagnosticCodes.Allocator, function,
                $"owned {valueName} has no allocator domain",
                configuration + ".AllocatorKind");
        if (plan.AllocatorKind is BindingAllocatorKind.NativeFunction or BindingAllocatorKind.Custom &&
            string.IsNullOrWhiteSpace(plan.AllocatorFunction))
            Add(module, BindingDiagnosticCodes.Allocator, function,
                $"owned {valueName} has no allocator function identity",
                configuration + ".AllocatorFunction");
        if (string.IsNullOrWhiteSpace(plan.CleanupFunction))
            Add(module, BindingDiagnosticCodes.Allocator, function,
                $"owned {valueName} has no cleanup function",
                configuration + ".CleanupFunction");
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
            function.SuppressFriendlySurface = true;
        BindingDiagnosticSeverity severity = config.StrictSafetySeverity == StrictSafetySeverity.Error
            ? BindingDiagnosticSeverity.Error
            : BindingDiagnosticSeverity.Warning;
        module.StructuredDiagnostics.Add(new(severity,
            $"{function.NativeName}: {reason}. Add the minimum explicit setting '{configuration}'. Raw ABI remains available; do not infer a friendly ownership API until configured.", code));
    }
}
