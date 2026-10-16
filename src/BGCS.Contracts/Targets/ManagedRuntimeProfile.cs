using System.Collections.Immutable;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Native;

namespace BGCS.Contracts.Targets;

/// <summary>Identifies the managed execution model independently from the native ABI and generator host.</summary>
public enum ManagedRuntimeKind
{
    /// <summary>The desktop managed runtime.</summary>
    CoreClr,
    /// <summary>A native ahead-of-time managed executable.</summary>
    NativeAot,
    /// <summary>The browser WebAssembly managed runtime.</summary>
    BrowserWasm
}

/// <summary>Describes how the generated context acquires native symbols.</summary>
public enum NativeModuleMechanism
{
    /// <summary>A loader supplies named exports from a native dynamic module.</summary>
    DynamicLibrary,
    /// <summary>A static native adapter supplies symbols linked into the application.</summary>
    StaticAdapter,
    /// <summary>The caller supplies an explicit table and its ownership protocol.</summary>
    ExternalFunctionTable
}

/// <summary>Describes how a runtime imports the independently established native calling convention.</summary>
public enum ManagedImportConventionStrategy
{
    /// <summary>Generated imports declare their exact unmanaged convention explicitly.</summary>
    Explicit,
    /// <summary>The browser wasm32 runtime's native import default is the C calling convention.</summary>
    BrowserCdeclDefault
}

/// <summary>Describes the runtime's implemented transport for a direct native function-pointer value at an import boundary.</summary>
public enum ManagedFunctionPointerTransport
{
    /// <summary>The physical import retains the exact typed unmanaged function-pointer carrier.</summary>
    Typed,
    /// <summary>The browser wasm32 static C ABI transports a function-table address as an explicit native integer; checked typed forwarders retain the callable contract.</summary>
    BrowserWasm32TableIndex
}

/// <summary>Names an implemented unmanaged aggregate calling algorithm, independently of platform certification.</summary>
public enum ManagedAggregateCallingAbi
{
    /// <summary>The Microsoft x64 aggregate argument and return convention.</summary>
    WindowsX64,
    /// <summary>The System V AMD64 eightbyte aggregate classification convention.</summary>
    SystemVAmd64
}

/// <summary>Constrains aggregate planning to a resolved native ABI and managed runtime implementation.</summary>
/// <param name="nativeTargetId">The exact concrete native target.</param>
/// <param name="triple">The resolved compiler target triple.</param>
/// <param name="pointerSize">The native pointer width.</param>
/// <param name="byteOrder">The native byte order.</param>
/// <param name="callingAbi">The implemented aggregate calling algorithm.</param>
/// <remarks>This descriptor grants no Verified evidence or platform certification. Unsupported storage shapes are still rejected by the backend.</remarks>
public sealed record ManagedAggregateAbiProfile(
    string nativeTargetId,
    string triple,
    int pointerSize,
    NativeByteOrder byteOrder,
    ManagedAggregateCallingAbi callingAbi
);

/// <summary>Freezes managed runtime constraints used by import, callback and lifecycle planning.</summary>
/// <param name="id">The complete stable profile identity.</param>
/// <param name="kind">The managed execution model.</param>
/// <param name="moduleMechanism">The default native symbol acquisition mechanism.</param>
/// <param name="importModes">Import mechanisms admitted by this profile's implemented adapter.</param>
/// <param name="requiresStaticCallbackThunks">Whether callback generation must use static AOT-compatible thunks.</param>
/// <param name="allowsBlockingDisposal">Whether cleanup is permitted to wait synchronously for native quiescence.</param>
public sealed record ManagedRuntimeProfile(
    string id,
    ManagedRuntimeKind kind,
    NativeModuleMechanism moduleMechanism,
    ImmutableArray<BindingImportMode> importModes,
    bool requiresStaticCallbackThunks,
    bool allowsBlockingDisposal
)
{
    /// <summary>Gets the implemented direct function-pointer import transport; it grants no aggregate ABI capability or platform certification.</summary>
    public ManagedFunctionPointerTransport functionPointerTransport { get; init; }
    /// <summary>Gets the implemented convention mechanism for source-generated native imports.</summary>
    public ManagedImportConventionStrategy libraryImportConvention { get; init; }
    /// <summary>Gets a resolved implemented aggregate calling profile, or null when aggregate calls require a native pointer bridge.</summary>
    public ManagedAggregateAbiProfile? aggregateAbi { get; init; }
}
