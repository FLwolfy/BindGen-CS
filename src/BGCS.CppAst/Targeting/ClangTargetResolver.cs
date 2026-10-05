using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using BGCS.Core.Targeting;
using BGCS.CppAst.Targeting.Providers;

namespace BGCS.CppAst.Targeting;

/// <summary>
/// Resolves open target IDs through an immutable set of native target providers.
/// </summary>
public sealed class ClangTargetResolver
{
    private readonly INativeTargetProvider[] m_providers;
    /// <summary>
    /// Composes the parser's built-in native target providers.
    /// </summary>
    public ClangTargetResolver() : this([new WindowsNativeTargetProvider(), new UnixNativeTargetProvider(), new AppleNativeTargetProvider(), new EmscriptenNativeTargetProvider()])
    {
    }

    /// <summary>
    /// Composes exactly the providers supplied by the caller.
    /// </summary>
    /// <param name = "providers">
    /// The providers whose target claims are checked for ambiguity before a result is returned.
    /// </param>
    /// <exception cref = "ArgumentNullException">
    /// The provider sequence or one of its elements is null.
    /// </exception>
    public ClangTargetResolver(IEnumerable<INativeTargetProvider> providers)
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
    public NativeTargetDescriptor Resolve(NativeTargetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        NativeTargetRequest concrete = request.targetId.value == "host" ? new(GetHostTargetId(), request.toolchain, request.tripleOverride) : request;
        NativeTargetDescriptor? resolved = null;
        foreach (INativeTargetProvider provider in m_providers)
        {
            if (!provider.TryResolve(concrete, out NativeTargetDescriptor? candidate))
                continue;
            if (candidate.targetId != concrete.targetId)
                throw new InvalidOperationException($"Provider '{provider.GetType().FullName}' changed target identity '{concrete.targetId}' to '{candidate.targetId}'.");
            if (resolved is not null)
                throw new InvalidOperationException($"Multiple native target providers claim '{concrete.targetId}'.");
            resolved = candidate;
        }

        return resolved ?? throw new ArgumentException($"Native target '{concrete.targetId}' is not valid for the registered target providers.", nameof(request));
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
            return new("linux-" + architecture + "-gnu");
        if (OperatingSystem.IsMacOS())
            return new("macos-" + architecture + "-darwin");
        if (OperatingSystem.IsFreeBSD())
            return new("freebsd-" + architecture + "-gnu");
        throw new PlatformNotSupportedException($"Unsupported generator host '{RuntimeInformation.OSDescription}'. Select an explicit native target ID.");
    }
}
