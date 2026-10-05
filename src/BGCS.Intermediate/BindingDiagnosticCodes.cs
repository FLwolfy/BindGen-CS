namespace BGCS.Intermediate;

/// <summary>
/// Defines stable identifiers for safety, unsupported semantics, and artifact publication failures.
/// </summary>
public static class BindingDiagnosticCodes
{
    /// <summary>
    /// Identifies a pointer whose ownership or cleanup policy is unknown.
    /// </summary>
    public const string C_OWNERSHIP = "BGCS-SAFETY-OWNERSHIP";

    /// <summary>
    /// Identifies a callback without a complete retention and unregister contract.
    /// </summary>
    public const string C_CALLBACKLIFETIME = "BGCS-SAFETY-CALLBACK";

    /// <summary>
    /// Identifies a callback whose permitted native invocation threads are unknown.
    /// </summary>
    public const string C_CALLBACKTHREADING = "BGCS-SAFETY-CALLBACK-THREAD";

    /// <summary>
    /// Identifies asynchronous work without a terminal completion lifetime contract.
    /// </summary>
    public const string C_ASYNCLIFETIME = "BGCS-SAFETY-ASYNC";

    /// <summary>
    /// Identifies a native buffer without a provable length or capacity relationship.
    /// </summary>
    public const string C_BUFFERLENGTH = "BGCS-SAFETY-LENGTH";

    /// <summary>
    /// Identifies an owned output without its matching native deallocator.
    /// </summary>
    public const string C_ALLOCATOR = "BGCS-SAFETY-ALLOCATOR";

    /// <summary>
    /// Identifies a lowering accepted through an explicitly enabled unsafe policy.
    /// </summary>
    public const string C_UNSAFELOWERING = "BGCS-SAFETY-LOWERING-BYPASS";

    /// <summary>
    /// Identifies an external managed carrier without sufficient target ABI evidence.
    /// </summary>
    public const string C_EXTERNALTYPE = "BGCS-SAFETY-EXTERNAL-TYPE";

    /// <summary>
    /// Identifies an IR declaration that the C# emitter cannot represent without losing semantics.
    /// </summary>
    public const string C_CSHARPUNSUPPORTED = "BGCSCS001";

    /// <summary>
    /// Identifies a C++ primary template that needs a requested concrete specialization.
    /// </summary>
    public const string C_CPPINSTANTIATION = "BGCSCPP-INSTANTIATION";

    /// <summary>
    /// Identifies a C++ declaration without an available safe C ABI lowering.
    /// </summary>
    public const string C_CPPUNSUPPORTED = "BGCSCPP001";

    /// <summary>
    /// Identifies a file-system failure while writing or publishing generated artifacts.
    /// </summary>
    public const string C_OUTPUTFAILURE = "BGCSIO001";
}
