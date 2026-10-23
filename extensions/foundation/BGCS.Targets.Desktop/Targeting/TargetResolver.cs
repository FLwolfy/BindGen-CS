using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;
using BGCS.Core.Targets;
using BGCS.Targets.Desktop.Targeting.Providers;
using BGCS.Targets.Desktop.Capabilities;

namespace BGCS.Targets.Desktop.Targeting;

/// <summary>
/// Resolves open target IDs through an immutable set of native target providers.
/// </summary>
public sealed class TargetResolver
{
    private readonly INativeTargetProvider[] m_providers;
    /// <summary>
    /// Composes the parser's built-in native target providers.
    /// </summary>
    public TargetResolver() : this(CreateBuiltInProviders())
    {
    }

    /// <summary>Creates independently owned providers for the built-in platform families.</summary>
    /// <returns>The built-in providers, suitable for composition with additional neutral providers.</returns>
    public static IReadOnlyList<INativeTargetProvider> CreateBuiltInProviders()
        => Array.AsReadOnly<INativeTargetProvider>([new WindowsNativeTargetProvider(), new UnixNativeTargetProvider(), new AppleNativeTargetProvider()]);

    /// <summary>
    /// Composes exactly the providers supplied by the caller.
    /// </summary>
    /// <param name = "providers">
    /// The providers whose target claims are checked for ambiguity before a result is returned.
    /// </param>
    /// <exception cref = "ArgumentNullException">
    /// The provider sequence or one of its elements is null.
    /// </exception>
    public TargetResolver(IEnumerable<INativeTargetProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        m_providers = providers.ToArray();
        foreach (INativeTargetProvider provider in m_providers)
            ArgumentNullException.ThrowIfNull(provider);
    }

    /// <summary>
    /// Resolves the request and rejects unsupported or multiply claimed target IDs.
    /// </summary>
    /// <param name = "request">
    /// The target identity and explicit compiler inputs. The host alias is resolved once before provider dispatch.
    /// </param>
    /// <returns>
    /// The immutable concrete target returned by exactly one provider.
    /// </returns>
    /// <exception cref = "ArgumentNullException">
    /// The request is null.
    /// </exception>
    /// <exception cref = "ArgumentException">
    /// No provider supports the requested target.
    /// </exception>
    /// <exception cref = "InvalidOperationException">
    /// Multiple providers claim the target, or a provider returns a different target identity.
    /// </exception>
    /// <exception cref = "PlatformNotSupportedException">
    /// The host alias cannot be resolved for this operating system or process architecture.
    /// </exception>
    /// <exception cref="NotSupportedException">A custom platform has no declared capability profile.</exception>
    public NativeTargetDescriptor Resolve(NativeTargetRequest request)
        => TryResolve(request, out NativeTargetDescriptor? target) ? target
            : throw new ArgumentException($"Native target '{request.targetId}' is not valid for the registered target providers.", nameof(request));

    /// <summary>Resolves exactly one selected family while allowing unrelated extensions to decline the same request.</summary>
    /// <param name="request">The explicit target and SDK requirements.</param>
    /// <param name="target">The resolved concrete target, or null when no maintained family owns this request.</param>
    /// <returns>Whether a maintained family owns the requested identity.</returns>
    public bool TryResolve(
        NativeTargetRequest request,
        [NotNullWhen(true)] out NativeTargetDescriptor? target
    ) {
        ArgumentNullException.ThrowIfNull(request);
        NativeTargetRequest concrete = request.targetId.value == "host" ? new(GetHostTargetId(), request.toolchain, request.tripleOverride) { language = request.language } : request;
        NativeTargetDescriptor? resolved = null;
        foreach (INativeTargetProvider provider in m_providers)
        {
            if (!provider.TryResolve(concrete, out NativeTargetDescriptor? candidate))
                continue;
            if (candidate.targetId != concrete.targetId)
                throw new InvalidOperationException($"Provider '{provider.GetType().FullName}' changed target identity '{concrete.targetId}' to '{candidate.targetId}'.");
            TargetCapabilities capabilities = PlatformCapabilityCatalog.Get(candidate);
            if (candidate.capabilities is null)
                candidate = new(candidate.targetId, candidate.platformId, candidate.architectureId, candidate.abiId,
                    candidate.triple, candidate.toolchain, capabilities, candidate.byteOrder)
                { headerSearchPaths = candidate.headerSearchPaths, sourceConventions = candidate.sourceConventions };
            if (resolved is not null)
                throw new InvalidOperationException($"Multiple native target providers claim '{concrete.targetId}'.");
            resolved = candidate;
        }

        target = resolved;
        return target is not null;
    }

    private static NativeTargetId GetHostTargetId()
    {
        string architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X86 => "x86",
            Architecture.X64 => "x64",
            Architecture.Arm => "arm",
            Architecture.Arm64 => "arm64",
            _ => throw new PlatformNotSupportedException($"Unsupported generator host architecture '{RuntimeInformation.ProcessArchitecture}'.")
        };
        if (OperatingSystem.IsWindows())
            return new("windows-" + architecture + "-msvc");
        if (OperatingSystem.IsLinux())
        {
            string rid = RuntimeInformation.RuntimeIdentifier;
            string abi = rid == "linux-musl-" + architecture ? "musl"
                : rid == "linux-" + architecture ? "gnu"
                : throw new PlatformNotSupportedException($"Linux host runtime '{rid}' does not establish GNU or musl ABI identity. Select an explicit native target ID.");
            return new("linux-" + architecture + "-" + abi);
        }
        if (OperatingSystem.IsMacOS())
            return new("macos-" + architecture + "-darwin");
        throw new PlatformNotSupportedException($"Unsupported generator host '{RuntimeInformation.OSDescription}'. Select an explicit native target ID.");
    }
}
