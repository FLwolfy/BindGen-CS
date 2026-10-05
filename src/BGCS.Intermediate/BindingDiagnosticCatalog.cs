using System;
using System.Collections.Generic;
using System.Linq;

namespace BGCS.Intermediate;

/// <summary>
/// Provides programmatic explanations for every diagnostic identifier emitted by the generators.
/// </summary>
public static class BindingDiagnosticCatalog
{
    private static readonly BindingDiagnosticDescriptor[] Descriptors = [
        new(BindingDiagnosticCodes.C_ALLOCATOR,
            "Output allocator is unknown",
            "An owned output needs cleanup but its native deallocator is not declared.",
            "Declare ownership, cleanupFunction, encoding, and requiresCleanup in the marshalling mapping."),
        new(BindingDiagnosticCodes.C_CALLBACKLIFETIME,
            "Callback lifetime is unknown",
            "A native callback has no declared retention, unregister, or user-data lifetime.",
            "Declare callbackRetention and unregister behavior; retain the delegate until native callbacks have stopped."),
        new(BindingDiagnosticCodes.C_CALLBACKTHREADING,
            "Callback threading is unknown",
            "The permitted native callback threads are not declared.",
            "Set callbackThreading and synchronize the state accessed by the callback."),
        new(BindingDiagnosticCodes.C_ASYNCLIFETIME,
            "Asynchronous completion lifetime is unknown",
            "Native work retains a callback or pointer beyond the initiating call without terminal completion.",
            "Declare asyncCompletion and completionFunction or use an application-owned lifetime token on the raw ABI."),
        new(BindingDiagnosticCodes.C_BUFFERLENGTH,
            "Buffer length relationship is unknown",
            "A native buffer's length, capacity, or written count cannot be proven.",
            "Declare lengthParameter, capacityParameter, and writtenCountParameter as applicable."),
        new(BindingDiagnosticCodes.C_OWNERSHIP,
            "Pointer ownership is unknown",
            "A pointer return has no declared borrowed, owned, transferred, shared, or caller-managed lifetime.",
            "Set ownership and provide cleanupFunction and requiresCleanup for owned values."),
        new(BindingDiagnosticCodes.C_EXTERNALTYPE,
            "External managed carrier requires ABI evidence",
            "A managed carrier crosses the ABI by value without matching target layout evidence.",
            "Declare externalTypeContracts with native aliases, size, alignment, and requireLayoutMatch."),
        new(BindingDiagnosticCodes.C_UNSAFELOWERING,
            "Unsafe lowering was explicitly enabled",
            "An unsafe lowering was accepted through loweringSafetyPolicy=AllowUnsafe.",
            "Keep independent ABI and lifetime invocation tests; prefer UserAsserted or Verified when supported by evidence."),
        new(BindingDiagnosticCodes.C_CSHARPUNSUPPORTED,
            "C# emitter cannot preserve the declaration",
            "The frozen binding IR contains semantics that the C# emitter cannot represent without loss.",
            "Implement a general IR lowering and emitter support. Unsupported declarations fail before output publication."),
        new(BindingDiagnosticCodes.C_CPPINSTANTIATION,
            "C++ template requires an explicit instance",
            "A primary class or function template has no requested concrete specialization.",
            "Add the required specialization to templateInstantiations or functionTemplateInstantiations."),
        new(BindingDiagnosticCodes.C_CPPUNSUPPORTED,
            "C++ declaration has no safe lowering",
            "No registered lowering can preserve a declaration's C ABI and lifetime.",
            "Add a declarative recipe, typed lowering plugin, template instance, or explicitly owned native shim."),
        new(BindingDiagnosticCodes.C_OUTPUTFAILURE,
            "Generated output could not be published",
            "Writing or committing generated artifacts failed at the file-system boundary.",
            "Check permissions, available space, and conflicting file handles. Failed publication preserves prior output.")
    ];

    private static readonly IReadOnlyDictionary<string, BindingDiagnosticDescriptor> ByCode =
        Descriptors.ToDictionary(static descriptor => descriptor.code, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets immutable diagnostic descriptors ordered by code.
    /// </summary>
    public static IReadOnlyList<BindingDiagnosticDescriptor> all { get; } =
        Array.AsReadOnly(Descriptors.OrderBy(static descriptor => descriptor.code, StringComparer.Ordinal).ToArray());

    /// <summary>
    /// Resolves a diagnostic explanation using a trimmed, case-insensitive identifier.
    /// </summary>
    /// <param name="code">Diagnostic identifier; null or whitespace is treated as absent.</param>
    /// <param name="descriptor">The matching explanation, or null when no identifier matches.</param>
    /// <returns>True when a descriptor was found; otherwise false.</returns>
    public static bool TryGet(
        string? code,
        out BindingDiagnosticDescriptor descriptor
    ) {
        if (string.IsNullOrWhiteSpace(code))
        {
            descriptor = null!;
            return false;
        }

        return ByCode.TryGetValue(code.Trim(), out descriptor!);
    }
}
