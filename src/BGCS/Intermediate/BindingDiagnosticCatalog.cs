namespace BGCS.Intermediate;

/// <summary>
/// Stable diagnostic identifiers emitted by BindGen-CS analysis and bridge generation.
/// </summary>
public static class BindingDiagnosticCodes
{
    public const string Ownership = "BGCS-SAFETY-OWNERSHIP";
    public const string CallbackLifetime = "BGCS-SAFETY-CALLBACK";
    public const string BufferLength = "BGCS-SAFETY-LENGTH";
    public const string Allocator = "BGCS-SAFETY-ALLOCATOR";
    public const string CSharpUnsupported = "BGCSCS001";
    public const string CppInstantiation = "BGCSCPP-INSTANTIATION";
    public const string CppUnsupported = "BGCSCPP001";
}

/// <summary>
/// Describes one stable diagnostic and the general remediation contract.
/// </summary>
/// <param name="Code">Stable machine-readable diagnostic identifier.</param>
/// <param name="Title">Short human-readable name.</param>
/// <param name="Cause">Condition that produces the diagnostic.</param>
/// <param name="Resolution">General corrective action. Individual diagnostics may include a more specific configuration path.</param>
public sealed record BindingDiagnosticDescriptor(string Code, string Title, string Cause, string Resolution);

/// <summary>
/// Provides versioned, programmatic documentation for stable BindGen-CS diagnostics.
/// </summary>
public static class BindingDiagnosticCatalog
{
    private static readonly BindingDiagnosticDescriptor[] descriptors =
    [
        new(
            BindingDiagnosticCodes.Allocator,
            "Output allocator is unknown",
            "An output string or buffer requires cleanup, but the native allocator/deallocator contract is not declared.",
            "Set the mapping ownership, cleanup function, encoding, and cleanup requirement. Do not assume that managed or C runtime free matches the native allocator."),
        new(
            BindingDiagnosticCodes.CallbackLifetime,
            "Callback lifetime is unknown",
            "A callback crosses the ABI boundary without a declared retention, unregister, or user-data lifetime contract.",
            "Add a parameter marshalling mapping and keep the delegate alive for the complete native retention period; expose unregister/dispose behavior when required."),
        new(
            BindingDiagnosticCodes.BufferLength,
            "Buffer length relationship is unknown",
            "A pointer appears to represent a buffer but no length, capacity, or written-count relationship can be proven.",
            "Set LengthParameter or CapacityParameter and, when applicable, WrittenCountParameter in the parameter marshalling mapping."),
        new(
            BindingDiagnosticCodes.Ownership,
            "Pointer ownership is unknown",
            "A pointer return does not declare whether it is borrowed, owned, transferred, shared, or caller-managed.",
            "Set the return Ownership and, for owned values, the CleanupFunction and RequiresCleanup contract."),
        new(
            BindingDiagnosticCodes.CSharpUnsupported,
            "C# emitter cannot preserve the declaration",
            "The IR-native C# emitter encountered a declaration whose ABI semantics it cannot emit without loss, such as a bitfield, variadic function, non-free callable, or unexposed type.",
            "Keep the compatibility emitter for this module or extend the shared IR and C# emitter with a general, tested lowering. The emitter fails before writing output instead of silently dropping semantics."),
        new(
            BindingDiagnosticCodes.CppInstantiation,
            "C++ template requires an explicit instance",
            "A primary class or function template was discovered without a concrete specialization requested by configuration.",
            "Add only the required fully qualified specialization to TemplateInstantiations or FunctionTemplateInstantiations."),
        new(
            BindingDiagnosticCodes.CppUnsupported,
            "C++ declaration has no safe lowering",
            "The bridge encountered a C++ type or callable that no registered lowering can represent with a proven C ABI and lifetime.",
            "Use a supported adapter, request an explicit template instance, or implement a general custom generation step/adapter that declares ownership and allocator semantics.")
    ];

    private static readonly IReadOnlyDictionary<string, BindingDiagnosticDescriptor> byCode =
        descriptors.ToDictionary(descriptor => descriptor.Code, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets all stable diagnostics ordered by code.
    /// </summary>
    public static IReadOnlyList<BindingDiagnosticDescriptor> All { get; } =
        Array.AsReadOnly(descriptors.OrderBy(descriptor => descriptor.Code, StringComparer.Ordinal).ToArray());

    /// <summary>
    /// Resolves a descriptor by code using case-insensitive matching.
    /// </summary>
    public static bool TryGet(string code, out BindingDiagnosticDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            descriptor = null!;
            return false;
        }
        return byCode.TryGetValue(code.Trim(), out descriptor!);
    }
}
