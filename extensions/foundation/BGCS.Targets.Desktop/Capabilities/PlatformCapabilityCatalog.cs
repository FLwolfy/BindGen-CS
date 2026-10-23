using System;
using BGCS.Core.Targets;

namespace BGCS.Targets.Desktop.Capabilities;

/// <summary>Reports parser and runtime platform mechanisms without claiming unexecuted platform certification.</summary>
public static class PlatformCapabilityCatalog
{
    /// <summary>Returns parser SDK and consumer module capabilities for a resolved native target family.</summary>
    /// <param name="target">The complete resolved target identity.</param>
    /// <returns>Declared adapter mechanisms; no validation stage is inferred from their existence.</returns>
    public static TargetCapabilities Get(NativeTargetDescriptor target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.capabilities is TargetCapabilities declared) return declared;
        return target.platformId switch
        {
            "windows" or "linux" or "macos" => new(false, true),
            _ => throw new NotSupportedException("The custom target must supply its own platform capability profile.")
        };
    }
}
