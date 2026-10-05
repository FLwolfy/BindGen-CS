using System;
using BGCS.Configuration;

namespace BGCS.Analysis;

using BGCS.Intermediate;

/// <summary>
/// Reports native semantics that cannot be proven from declarations and require minimal explicit configuration.
/// </summary>
internal sealed class StrictSafetyAnalyzer
{
    private readonly CsCodeGeneratorConfig m_config;
    public StrictSafetyAnalyzer(CsCodeGeneratorConfig config)
    {
        this.m_config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public void Analyze(BindingModuleBuilder module)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (!this.m_config.strictSafety)
            return;
        foreach (BindingFunctionBuilder function in module.functions)
        {
            this.m_config.marshallingMappings.TryGetValue(function.nativeName, out FunctionMarshallingMapping? mapping);
            if (function.returnType.pointerDepth > 0 && mapping?.@return?.ownership == null)
                Add(module, BindingDiagnosticCodes.C_OWNERSHIP, function, "pointer return ownership is not declared", $"MarshallingMappings.{function.nativeName}.Return.Ownership");
            for (int i = 0; i < function.parameters.Count; i++)
            {
                BindingParameter parameter = function.parameters[i];
                MarshallingMapping? parameterMapping = null;
                mapping?.parameters.TryGetValue(parameter.nativeName, out parameterMapping);
                if (parameter.marshalling.strategy == MarshallingStrategy.Callback)
                {
                    if (parameter.marshalling.callbackLifetime == BindingCallbackLifetime.Unspecified)
                        Add(module, BindingDiagnosticCodes.C_CALLBACKLIFETIME, function, $"callback parameter '{parameter.nativeName}' retention lifetime is not declared", $"MarshallingMappings.{function.nativeName}.Parameters.{parameter.nativeName}.CallbackLifetime");
                    if (parameter.marshalling.callbackThreading == BindingCallbackThreading.Unspecified)
                        Add(module, BindingDiagnosticCodes.C_CALLBACKTHREADING, function, $"callback parameter '{parameter.nativeName}' invocation threading is not declared", $"MarshallingMappings.{function.nativeName}.Parameters.{parameter.nativeName}.CallbackThreading");
                    if (parameter.marshalling.callbackLifetime == BindingCallbackLifetime.RetainedUntilUnregister && string.IsNullOrWhiteSpace(parameter.marshalling.unregisterFunction))
                        Add(module, BindingDiagnosticCodes.C_CALLBACKLIFETIME, function, $"retained callback parameter '{parameter.nativeName}' has no synchronous unregister function", $"MarshallingMappings.{function.nativeName}.Parameters.{parameter.nativeName}.UnregisterFunction");
                    if (parameter.marshalling.callbackLifetime == BindingCallbackLifetime.RetainedUntilCompletion && parameter.marshalling.asyncCompletion == BindingAsyncCompletion.None)
                        Add(module, BindingDiagnosticCodes.C_ASYNCLIFETIME, function, $"asynchronously retained callback parameter '{parameter.nativeName}' has no completion mechanism", $"MarshallingMappings.{function.nativeName}.Parameters.{parameter.nativeName}.AsyncCompletion");
                    if (parameter.marshalling.asyncCompletion != BindingAsyncCompletion.None && string.IsNullOrWhiteSpace(parameter.marshalling.completionFunction))
                        Add(module, BindingDiagnosticCodes.C_ASYNCLIFETIME, function, $"asynchronous callback parameter '{parameter.nativeName}' has no terminal completion function", $"MarshallingMappings.{function.nativeName}.Parameters.{parameter.nativeName}.CompletionFunction");
                }

                if (LooksLikeBuffer(parameter) && parameter.marshalling.lengthParameter == null && parameter.marshalling.capacityParameter == null && parameterMapping == null)
                    Add(module, BindingDiagnosticCodes.C_BUFFERLENGTH, function, $"buffer parameter '{parameter.nativeName}' has no proven length or capacity relationship", $"MarshallingMappings.{function.nativeName}.Parameters.{parameter.nativeName}.LengthParameter");
                if (parameter.marshalling.strategy == MarshallingStrategy.String && parameter.direction != BindingDirection.In && parameter.marshalling.cleanupFunction == null && parameterMapping == null)
                    Add(module, BindingDiagnosticCodes.C_ALLOCATOR, function, $"output string parameter '{parameter.nativeName}' has no cleanup allocator", $"MarshallingMappings.{function.nativeName}.Parameters.{parameter.nativeName}.CleanupFunction");
                ValidateOwnedAllocator(module, function, parameter.nativeName, parameter.marshalling, $"MarshallingMappings.{function.nativeName}.Parameters.{parameter.nativeName}");
            }

            ValidateOwnedAllocator(module, function, "return value", function.returnMarshalling, $"MarshallingMappings.{function.nativeName}.Return");
        }
    }

    private void ValidateOwnedAllocator(
        BindingModuleBuilder module,
        BindingFunctionBuilder function,
        string valueName,
        MarshallingPlan plan,
        string configuration
    ) {
        if (plan.ownership != BindingOwnership.Owned && !plan.requiresCleanup)
            return;
        if (plan.allocatorKind == BindingAllocatorKind.Unspecified)
            Add(module, BindingDiagnosticCodes.C_ALLOCATOR, function, $"owned {valueName} has no allocator domain", configuration + ".AllocatorKind");
        if (plan.allocatorKind is BindingAllocatorKind.NativeFunction or BindingAllocatorKind.Custom && string.IsNullOrWhiteSpace(plan.allocatorFunction))
            Add(module, BindingDiagnosticCodes.C_ALLOCATOR, function, $"owned {valueName} has no allocator function identity", configuration + ".AllocatorFunction");
        if (string.IsNullOrWhiteSpace(plan.cleanupFunction))
            Add(module, BindingDiagnosticCodes.C_ALLOCATOR, function, $"owned {valueName} has no cleanup function", configuration + ".CleanupFunction");
    }

    private static bool LooksLikeBuffer(BindingParameter parameter)
    {
        if (parameter.type.pointerDepth == 0)
            return false;
        string name = parameter.nativeName;
        return name.Contains("buffer", StringComparison.OrdinalIgnoreCase) || name.Contains("data", StringComparison.OrdinalIgnoreCase) || name.Contains("items", StringComparison.OrdinalIgnoreCase) || name.Contains("values", StringComparison.OrdinalIgnoreCase) || name.Contains("output", StringComparison.OrdinalIgnoreCase);
    }

    private void Add(
        BindingModuleBuilder module,
        string code,
        BindingFunctionBuilder function,
        string reason,
        string configuration
    ) {
        if (this.m_config.strictSafetySeverity is StrictSafetySeverity.SuppressFriendly or StrictSafetySeverity.Error)
            function.suppressFriendlySurface = true;
        BindingDiagnosticSeverity severity = this.m_config.strictSafetySeverity == StrictSafetySeverity.Error ? BindingDiagnosticSeverity.Error : BindingDiagnosticSeverity.Warning;
        module.structuredDiagnostics.Add(new(severity, $"{function.nativeName}: {reason}. Add the minimum explicit setting '{configuration}'. Raw ABI remains available; do not infer a friendly ownership API until configured.", code));
    }
}
