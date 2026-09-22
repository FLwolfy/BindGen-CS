using System;
using System.Runtime.InteropServices;
using BGCS.CppAst.Parsing;

namespace BGCS.CppAst.Targeting;

/// <summary>
/// Describes a fully resolved native compilation target.
/// </summary>
/// <param name="Platform">Resolved operating-system family.</param>
/// <param name="Architecture">Resolved processor architecture.</param>
/// <param name="Abi">Resolved native ABI family.</param>
/// <param name="Cpu">Clang CPU representation.</param>
/// <param name="Triple">Clang target triple.</param>
/// <param name="Identifier">Stable human-readable target identifier.</param>
public sealed record CppTarget(
    CppTargetPlatform Platform,
    CppTargetArchitecture Architecture,
    CppTargetAbi Abi,
    CppTargetCpu Cpu,
    string Triple,
    string Identifier)
{
    /// <summary>
    /// Resolves host aliases, validates the requested combination, and creates a concrete target.
    /// </summary>
    /// <param name="platform">Requested operating-system family.</param>
    /// <param name="architecture">Requested processor architecture.</param>
    /// <param name="abi">Requested ABI family.</param>
    /// <param name="tripleOverride">Optional explicit Clang target triple.</param>
    /// <returns>A validated concrete native target.</returns>
    /// <exception cref="PlatformNotSupportedException">The host operating system or architecture is unsupported.</exception>
    /// <exception cref="ArgumentException">The requested platform, architecture, and ABI combination is invalid.</exception>
    public static CppTarget Resolve(
        CppTargetPlatform platform = CppTargetPlatform.Host,
        CppTargetArchitecture architecture = CppTargetArchitecture.Host,
        CppTargetAbi abi = CppTargetAbi.Default,
        string? tripleOverride = null)
    {
        CppTargetPlatform resolvedPlatform = platform == CppTargetPlatform.Host ? ResolveHostPlatform() : platform;
        CppTargetArchitecture resolvedArchitecture = architecture == CppTargetArchitecture.Host
            ? ResolveHostArchitecture()
            : architecture;
        CppTargetAbi resolvedAbi = abi == CppTargetAbi.Default ? GetDefaultAbi(resolvedPlatform) : abi;
        Validate(resolvedPlatform, resolvedArchitecture, resolvedAbi);

        CppTargetCpu cpu = resolvedArchitecture switch
        {
            CppTargetArchitecture.X86 => CppTargetCpu.X86,
            CppTargetArchitecture.X64 => CppTargetCpu.X86_64,
            CppTargetArchitecture.Arm => CppTargetCpu.ARM,
            CppTargetArchitecture.Arm64 => CppTargetCpu.ARM64,
            _ => throw new ArgumentOutOfRangeException(nameof(architecture), resolvedArchitecture, null)
        };
        string triple = string.IsNullOrWhiteSpace(tripleOverride)
            ? BuildTriple(resolvedPlatform, resolvedArchitecture, resolvedAbi)
            : tripleOverride.Trim();
        string identifier = $"{GetPlatformName(resolvedPlatform)}-{GetArchitectureName(resolvedArchitecture)}-{GetAbiName(resolvedAbi)}";
        return new(resolvedPlatform, resolvedArchitecture, resolvedAbi, cpu, triple, identifier);
    }

    private static CppTargetPlatform ResolveHostPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return CppTargetPlatform.Windows;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return CppTargetPlatform.Linux;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return CppTargetPlatform.MacOS;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD))
            return CppTargetPlatform.FreeBSD;
        throw new PlatformNotSupportedException($"Unsupported host operating system '{RuntimeInformation.OSDescription}'. Specify an explicit target triple if Clang supports it.");
    }

    private static CppTargetArchitecture ResolveHostArchitecture()
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X86 => CppTargetArchitecture.X86,
            System.Runtime.InteropServices.Architecture.X64 => CppTargetArchitecture.X64,
            System.Runtime.InteropServices.Architecture.Arm => CppTargetArchitecture.Arm,
            System.Runtime.InteropServices.Architecture.Arm64 => CppTargetArchitecture.Arm64,
            _ => throw new PlatformNotSupportedException($"Unsupported host process architecture '{RuntimeInformation.ProcessArchitecture}'.")
        };
    }

    private static CppTargetAbi GetDefaultAbi(CppTargetPlatform platform)
    {
        return platform switch
        {
            CppTargetPlatform.Windows => CppTargetAbi.Msvc,
            CppTargetPlatform.Linux or CppTargetPlatform.FreeBSD => CppTargetAbi.Gnu,
            CppTargetPlatform.MacOS or CppTargetPlatform.IOS => CppTargetAbi.Darwin,
            CppTargetPlatform.Android => CppTargetAbi.Android,
            _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, null)
        };
    }

    private static void Validate(CppTargetPlatform platform, CppTargetArchitecture architecture, CppTargetAbi abi)
    {
        bool validAbi = platform switch
        {
            CppTargetPlatform.Windows => abi == CppTargetAbi.Msvc,
            CppTargetPlatform.Linux => abi is CppTargetAbi.Gnu or CppTargetAbi.Musl,
            CppTargetPlatform.MacOS or CppTargetPlatform.IOS => abi == CppTargetAbi.Darwin,
            CppTargetPlatform.Android => abi == CppTargetAbi.Android,
            CppTargetPlatform.FreeBSD => abi == CppTargetAbi.Gnu,
            _ => false
        };
        if (!validAbi)
            throw new ArgumentException($"ABI '{abi}' is not valid for platform '{platform}'.", nameof(abi));

        bool validArchitecture = platform switch
        {
            CppTargetPlatform.Windows => architecture is CppTargetArchitecture.X86 or CppTargetArchitecture.X64 or CppTargetArchitecture.Arm64,
            CppTargetPlatform.MacOS or CppTargetPlatform.IOS => architecture is CppTargetArchitecture.X64 or CppTargetArchitecture.Arm64,
            _ => architecture is CppTargetArchitecture.X86 or CppTargetArchitecture.X64 or CppTargetArchitecture.Arm or CppTargetArchitecture.Arm64
        };
        if (!validArchitecture)
            throw new ArgumentException($"Architecture '{architecture}' is not valid for platform '{platform}'.", nameof(architecture));
    }

    private static string BuildTriple(CppTargetPlatform platform, CppTargetArchitecture architecture, CppTargetAbi abi)
    {
        string cpu = architecture switch
        {
            CppTargetArchitecture.X86 => "i686",
            CppTargetArchitecture.X64 => "x86_64",
            CppTargetArchitecture.Arm => "armv7",
            CppTargetArchitecture.Arm64 when platform is CppTargetPlatform.MacOS or CppTargetPlatform.IOS => "arm64",
            CppTargetArchitecture.Arm64 => "aarch64",
            _ => throw new ArgumentOutOfRangeException(nameof(architecture), architecture, null)
        };
        return platform switch
        {
            CppTargetPlatform.Windows => $"{cpu}-pc-windows-msvc",
            CppTargetPlatform.Linux when abi == CppTargetAbi.Musl => $"{cpu}-unknown-linux-musl",
            CppTargetPlatform.Linux => $"{cpu}-unknown-linux-gnu",
            CppTargetPlatform.MacOS => $"{cpu}-apple-darwin",
            CppTargetPlatform.Android => $"{cpu}-linux-android",
            CppTargetPlatform.IOS => $"{cpu}-apple-ios",
            CppTargetPlatform.FreeBSD => $"{cpu}-unknown-freebsd",
            _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, null)
        };
    }

    private static string GetPlatformName(CppTargetPlatform platform) => platform switch
    {
        CppTargetPlatform.MacOS => "macos",
        CppTargetPlatform.IOS => "ios",
        _ => platform.ToString().ToLowerInvariant()
    };

    private static string GetArchitectureName(CppTargetArchitecture architecture) => architecture switch
    {
        CppTargetArchitecture.Arm64 => "arm64",
        CppTargetArchitecture.Arm => "arm",
        CppTargetArchitecture.X64 => "x64",
        CppTargetArchitecture.X86 => "x86",
        _ => architecture.ToString().ToLowerInvariant()
    };

    private static string GetAbiName(CppTargetAbi abi) => abi.ToString().ToLowerInvariant();
}
