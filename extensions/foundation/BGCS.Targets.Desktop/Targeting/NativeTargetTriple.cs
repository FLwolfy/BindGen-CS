using System;
using System.Text.RegularExpressions;
using BGCS.Core.Targets;

namespace BGCS.Targets.Desktop.Targeting;

internal static class NativeTargetTriple
{
    internal static string Resolve(
        NativeTargetRequest request,
        string fallback,
        string platform,
        string architecture,
        string abi
    ) {
        if (request.tripleOverride is not string triple) return fallback;
        int separator = triple.IndexOf('-');
        string cpu = separator < 0 ? triple : triple[..separator];
        bool sameCpu = architecture switch
        {
            "x64" => cpu == "x86_64",
            "x86" => cpu is "i386" or "i486" or "i586" or "i686",
            "arm64" => cpu is "arm64" or "aarch64",
            "arm" => cpu is "arm" or "armv7" or "armv7a" or "thumb" or "thumbv7" or "thumbv7a",
            "wasm32" => cpu == "wasm32",
            _ => false
        };
        const string version = "(?:[0-9]+(?:\\.[0-9]+){0,3})?";
        string? tail = (platform, abi) switch
        {
            ("windows", "msvc") => "(?:[A-Za-z0-9_]+-)?windows-msvc",
            ("linux", "gnu") => "(?:[A-Za-z0-9_]+-)?linux-gnu",
            ("linux", "musl") => "(?:[A-Za-z0-9_]+-)?linux-musl",
            ("macos", "darwin") => "apple-(?:darwin|macosx|macos)" + version,
            _ => null
        };
        if (!sameCpu || separator < 0 || tail is null || !Regex.IsMatch(triple[(separator + 1)..], "\\A(?:" + tail + ")\\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking))
            throw new ArgumentException("Explicit compiler triple '" + triple + "' is incompatible with the CPU, platform or ABI of native target '" + request.targetId.value + "'.", nameof(request));
        return triple;
    }
}
