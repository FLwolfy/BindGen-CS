using System;
using System.Linq;
using BGCS.Contracts;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.Contracts.Native;
using BGCS.Platforms.Capabilities;

namespace BGCS.Platforms;

/// <summary>Resolves managed execution constraints independently of the generator host and target compiler.</summary>
public static class ManagedRuntimeResolver
{
    /// <summary>Resolves symbol acquisition and lifecycle constraints for a concrete runtime/target pair.</summary>
    /// <param name="target">The concrete native ABI.</param>
    /// <param name="runtime">The consumer's explicit managed execution model.</param>
    /// <param name="outputKind">The native consumption form, or null for the target's declared default.</param>
    /// <returns>The implemented runtime profile, without a certification claim.</returns>
    public static ManagedRuntimeProfile Resolve(
        NativeTargetDescriptor target,
        ManagedRuntimeKind runtime,
        NativeOutputKind? outputKind = null
    ) {
        ArgumentNullException.ThrowIfNull(target);
        if (runtime == ManagedRuntimeKind.BrowserWasm && (target.platformId != "emscripten" || target.architectureId != "wasm32" || target.abiId != "emscripten"))
            throw new NotSupportedException("Browser Wasm requires the implemented Emscripten wasm32 target ABI.");
        if (!Enum.IsDefined(runtime)) throw new ArgumentOutOfRangeException(nameof(runtime));
        if (target.platformId == "emscripten" && runtime != ManagedRuntimeKind.BrowserWasm || target.platformId != "emscripten" && runtime == ManagedRuntimeKind.BrowserWasm)
            throw new NotSupportedException("Browser Wasm requires an Emscripten wasm32 native target and its static native adapter.");
        TargetCapabilities capabilities = PlatformCapabilityCatalog.Get(target);
        NativeOutputKind actualOutput = outputKind ?? capabilities.defaultOutputKind;
        if (!Enum.IsDefined(actualOutput) || !capabilities.outputKinds.Contains(actualOutput))
            throw new NotSupportedException("The native output form is absent from the target's implemented construction mechanisms.");
        bool staticModule = !capabilities.supportsDynamicModules || actualOutput != NativeOutputKind.SharedLibrary;
        if (staticModule && runtime == ManagedRuntimeKind.CoreClr) throw new NotSupportedException("This static native target requires its AOT or browser runtime profile.");
        return new(target.targetId.value + "/" + runtime.ToString() + "/" + actualOutput.ToString(), runtime, staticModule ? NativeModuleMechanism.StaticAdapter : NativeModuleMechanism.DynamicLibrary,
            [BindingImportMode.FunctionTable, BindingImportMode.DllImport, BindingImportMode.LibraryImport], runtime != ManagedRuntimeKind.CoreClr, runtime != ManagedRuntimeKind.BrowserWasm)
        {
            functionPointerTransport = runtime == ManagedRuntimeKind.BrowserWasm ? ManagedFunctionPointerTransport.BrowserWasm32TableIndex : ManagedFunctionPointerTransport.Typed,
            libraryImportConvention = runtime == ManagedRuntimeKind.BrowserWasm ? ManagedImportConventionStrategy.BrowserCdeclDefault : ManagedImportConventionStrategy.Explicit,
            aggregateAbi = AggregateAbi(target, runtime)
        };
    }

    private static ManagedAggregateAbiProfile? AggregateAbi(
        NativeTargetDescriptor target,
        ManagedRuntimeKind runtime
    ) {
        if (runtime == ManagedRuntimeKind.BrowserWasm || target.byteOrder != NativeByteOrder.LittleEndian
            || target.architectureId != "x64" || !target.triple.StartsWith("x86_64-", StringComparison.Ordinal)) return null;
        ManagedAggregateCallingAbi? family = (target.platformId, target.abiId) switch
        {
            ("windows", "msvc") when target.triple.Contains("windows-msvc", StringComparison.Ordinal) => ManagedAggregateCallingAbi.WindowsX64,
            ("linux", "gnu") when target.triple.Contains("linux-gnu", StringComparison.Ordinal) => ManagedAggregateCallingAbi.SystemVAmd64,
            ("macos", "darwin") when target.triple.Contains("darwin", StringComparison.Ordinal) || target.triple.Contains("macos", StringComparison.Ordinal) => ManagedAggregateCallingAbi.SystemVAmd64,
            _ => null
        };
        return family is null ? null : new(target.targetId.value, target.triple, 8, target.byteOrder, family.Value);
    }
}
