using System.Collections.Immutable;
using BGCS.Core.Calls;
using BGCS.Core.Native;
using BGCS.Core.Requests;

namespace BGCS.Core.Targets;

/// <summary>Describes native symbol acquisition without naming an operating system.</summary>
public enum NativeModuleMechanism
{
    /// <summary>A loader supplies named exports from a native module.</summary>
    DynamicLibrary,
    /// <summary>A linked adapter supplies an explicitly described export table.</summary>
    StaticAdapter,
    /// <summary>The caller supplies a table and its ownership protocol.</summary>
    ExternalFunctionTable
}

/// <summary>Describes how imports express their native calling convention.</summary>
public enum ManagedImportConventionStrategy
{
    /// <summary>Each import declares its exact convention.</summary>
    Explicit,
    /// <summary>The runtime provider establishes one exact default convention.</summary>
    RuntimeDefault
}

/// <summary>Describes physical function-address transport across an import boundary.</summary>
public enum ManagedFunctionPointerTransport
{
    /// <summary>The import retains the typed unmanaged function pointer.</summary>
    Typed,
    /// <summary>The import transports an address-sized integer with checked typed forwarders.</summary>
    NativeInteger
}

/// <summary>Freezes execution capabilities supplied by an independently selected runtime provider.</summary>
/// <param name="id">The complete profile identity, including implementation revision.</param>
/// <param name="runtimeId">The open managed execution-model identity.</param>
/// <param name="moduleMechanism">The implemented symbol acquisition mechanism.</param>
/// <param name="importModes">The admitted import mechanisms.</param>
/// <param name="requiresStaticCallbackThunks">Whether reverse calls require static thunks.</param>
/// <param name="allowsBlockingDisposal">Whether cleanup can wait for native quiescence.</param>
/// <remarks>Capabilities declare implementations, not runtime certification or verified ABI evidence.</remarks>
public sealed record ManagedRuntimeProfile(
    string id,
    ManagedRuntimeId runtimeId,
    NativeModuleMechanism moduleMechanism,
    ImmutableArray<BindingImportMode> importModes,
    bool requiresStaticCallbackThunks,
    bool allowsBlockingDisposal
)
{
    /// <summary>Gets the exact native target to which the profile is bound.</summary>
    public string nativeTargetId { get; init; } = string.Empty;
    /// <summary>Gets the established compiler triple.</summary>
    public string nativeTriple { get; init; } = string.Empty;
    /// <summary>Gets the native pointer width in bytes.</summary>
    public int pointerSize { get; init; }
    /// <summary>Gets the native byte order.</summary>
    public NativeByteOrder byteOrder { get; init; }
    /// <summary>Gets the explicitly selected calling-ABI implementation identity.</summary>
    public CallAbiId callAbi { get; init; }
    /// <summary>Gets direct function-address import transport.</summary>
    public ManagedFunctionPointerTransport functionPointerTransport { get; init; }
    /// <summary>Gets the source-generated import convention mechanism.</summary>
    public ManagedImportConventionStrategy libraryImportConvention { get; init; }
    /// <summary>Gets the exact convention established when RuntimeDefault is selected.</summary>
    public string? defaultCallingConvention { get; init; }
}
