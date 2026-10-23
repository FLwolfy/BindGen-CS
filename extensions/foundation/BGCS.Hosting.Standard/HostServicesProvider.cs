using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BGCS.Core.Common;
using BGCS.Core.Hosting;
using BGCS.Core.Plugins;
using BGCS.Infrastructure.IO;

namespace BGCS.Hosting.Standard;

/// <summary>Observes host process and filesystem facilities without selecting a generated native target.</summary>
public sealed class HostServicesProvider : IHostServicesProvider, ICacheFingerprintProvider
{
    /// <summary>Creates independently owned host services for a frozen session.</summary>
    /// <param name="request">The explicit bootstrap directory and detached settings.</param>
    /// <returns>The observed path and managed host facilities.</returns>
    public HostServices Create(HostServicesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Path.IsPathFullyQualified(request.baseDirectory))
            throw new ArgumentException("Host bootstrap requires an absolute base directory.", nameof(request));
        foreach (var setting in request.settings.EnumerateObject())
            throw new ArgumentException("The host provider does not recognize setting '" + setting.Name + "'.", nameof(request));
        IHostPathService paths = new HostPathService(OperatingSystem.IsWindows()
            ? Windows.WindowsDirectorySemantics.Observe
            : OperatingSystem.IsMacOS() ? Unix.AppleDirectorySemantics.Observe
            : OperatingSystem.IsLinux() ? Unix.LinuxDirectorySemantics.Observe
            : throw new PlatformNotSupportedException("No observed host path implementation matches this host; register an explicit host provider."), Classify);
        string framework = Path.GetDirectoryName(typeof(object).Assembly.Location)
            ?? throw new InvalidOperationException("The host has no physical managed runtime installation.");
        string install = Directory.GetParent(framework)?.Parent?.Parent?.FullName
            ?? throw new InvalidOperationException("The managed runtime installation has no process host root.");
        string expected = Path.Combine(install, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        string selected = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? expected;
        if (!File.Exists(selected) || !paths.Equals(PhysicalPathResolver.Resolve(selected, paths), PhysicalPathResolver.Resolve(expected, paths)))
            throw new InvalidOperationException("The selected managed process host does not match the running managed runtime installation.");
        return new(paths, new(RuntimeInformation.RuntimeIdentifier, Path.GetFullPath(selected), !OperatingSystem.IsWindows()));
    }

    /// <summary>Contributes the provider's stable behavior identity; implementation bytes are captured independently.</summary>
    /// <param name="context">The selected target and immutable extension settings.</param>
    /// <param name="cancellationToken">Cancels fingerprint capture.</param>
    /// <returns>The host provider behavior revision and complete external input declarations.</returns>
    public CacheFingerprintContribution CaptureFingerprint(
        CacheFingerprintContext context,
        CancellationToken cancellationToken
    ) {
        cancellationToken.ThrowIfCancellationRequested();
        ContentDigest digest = new(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("observed-host-paths-v2/" + RuntimeInformation.RuntimeIdentifier))));
        return new(digest, []);
    }

    private static HostIoFailureKind Classify(Exception failure)
    {
        int code = failure.HResult & 0xffff;
        if (failure is IOException && (OperatingSystem.IsWindows() ? code is 32 or 33 : code == (OperatingSystem.IsMacOS() ? 35 : 11))) return HostIoFailureKind.SharingViolation;
        return OperatingSystem.IsWindows() && failure is IOException or UnauthorizedAccessException && code == 5
            ? HostIoFailureKind.TemporaryAccess : HostIoFailureKind.Permanent;
    }
}
