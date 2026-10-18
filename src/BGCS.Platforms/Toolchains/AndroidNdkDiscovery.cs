using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using BGCS.Contracts.Targets;

namespace BGCS.Platforms.Toolchains;

/// <summary>Resolves an explicitly installed Android NDK, exact API level and host compiler.</summary>
public static class AndroidNdkDiscovery
{
    /// <summary>Resolves target headers, libraries and a versioned Android compiler triple.</summary>
    /// <param name="ndkRoot">The existing NDK root containing source.properties.</param>
    /// <param name="architecture">arm64 or x64.</param>
    /// <param name="apiLevel">The explicit Android minimum API level.</param>
    /// <returns>An immutable target with the selected NDK version and API encoded in its triple.</returns>
    public static NativeTargetDescriptor Resolve(
        string ndkRoot,
        string architecture,
        int apiLevel
    ) {
        if (architecture is not ("arm64" or "x64")) throw new NotSupportedException("The built-in Android NDK profile supports arm64 and x64.");
        if (apiLevel < 21 || apiLevel > 999) throw new ArgumentOutOfRangeException(nameof(apiLevel), "64-bit Android requires an explicit supported API level of at least 21.");
        string root = Path.GetFullPath(ndkRoot), properties = Path.Combine(root, "source.properties");
        if (!File.Exists(properties)) throw new FileNotFoundException("The configured Android NDK has no version manifest.", properties);
        string revision = File.ReadLines(properties).Select(line => line.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(pair => pair.Length == 2 && pair[0] == "Pkg.Revision").Select(pair => pair[1]).SingleOrDefault()
            ?? throw new InvalidDataException("The Android NDK manifest has no unique Pkg.Revision.");
        if (!Version.TryParse(revision, out _)) throw new InvalidDataException("The Android NDK revision is not a structured version.");
        string host = OperatingSystem.IsWindows() ? "windows-x86_64" : OperatingSystem.IsMacOS() ? "darwin-x86_64" : OperatingSystem.IsLinux() ? "linux-x86_64"
            : throw new PlatformNotSupportedException("The NDK has no built-in host-tool profile for this generator host.");
        string prebuilt = Path.Combine(root, "toolchains", "llvm", "prebuilt", host);
        string compiler = Path.Combine(prebuilt, "bin", OperatingSystem.IsWindows() ? "clang++.exe" : "clang++");
        string sysroot = Path.Combine(prebuilt, "sysroot"), includes = Path.Combine(sysroot, "usr", "include"), cxx = Path.Combine(includes, "c++", "v1");
        string baseTriple = architecture == "arm64" ? "aarch64-linux-android" : "x86_64-linux-android";
        string architectureHeaders = Path.Combine(includes, baseTriple);
        if (!new[] { sysroot, includes, cxx, architectureHeaders }.All(Directory.Exists))
            throw new IOException("The configured NDK has no complete parser headers for the selected architecture/API level.");
        NativeToolchainDescriptor toolchain = new(compiler, sysroot, [includes, architectureHeaders], cxxSystemIncludeFolders: [cxx], sdkVersion: revision);
        return new(new("android-" + architecture), "android", architecture, "android", baseTriple + apiLevel.ToString(System.Globalization.CultureInfo.InvariantCulture), toolchain, byteOrder: BGCS.Contracts.Native.NativeByteOrder.LittleEndian);
    }
}
