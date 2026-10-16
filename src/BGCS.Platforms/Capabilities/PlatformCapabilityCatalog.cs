using System;
using System.Linq;
using BGCS.Contracts.Targets;
using BGCS.Contracts.Requests;

namespace BGCS.Platforms.Capabilities;

/// <summary>Reports implemented platform mechanisms without claiming unexecuted platform certification.</summary>
public static class PlatformCapabilityCatalog
{
    /// <summary>Returns construction and asset mechanisms for a resolved native target family.</summary>
    /// <param name="target">The complete resolved target identity.</param>
    /// <returns>Declared adapter mechanisms; no validation stage is inferred from their existence.</returns>
    public static TargetCapabilities Get(NativeTargetDescriptor target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.capabilities is TargetCapabilities declared)
        {
            if (declared.outputKinds.IsDefaultOrEmpty || declared.outputKinds.Any(kind => !Enum.IsDefined(kind))
                || !declared.outputKinds.Contains(declared.defaultOutputKind) || string.IsNullOrWhiteSpace(declared.assetLayout))
                throw new InvalidOperationException("A custom target capability profile has incomplete or contradictory construction mechanisms.");
            return declared;
        }
        return target.platformId switch
        {
            "windows" or "linux" or "macos" or "freebsd" => new([NativeOutputKind.SharedLibrary, NativeOutputKind.StaticLibrary, NativeOutputKind.Object], NativeOutputKind.SharedLibrary, false, true, "nugetNative"),
            "android" => new([NativeOutputKind.SharedLibrary, NativeOutputKind.StaticLibrary, NativeOutputKind.Object], NativeOutputKind.SharedLibrary, true, true, "androidNative"),
            "ios" or "ios-simulator" => new([NativeOutputKind.StaticLibrary, NativeOutputKind.Object], NativeOutputKind.StaticLibrary, true, false, "nativeReference"),
            "emscripten" => new([NativeOutputKind.StaticLibrary, NativeOutputKind.Object], NativeOutputKind.StaticLibrary, true, false, "nativeFileReference"),
            _ => throw new NotSupportedException("The custom target must supply its own platform capability profile.")
        };
    }
}
