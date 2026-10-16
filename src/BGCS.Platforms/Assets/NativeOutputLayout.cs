using System;
using System.IO;
using System.Linq;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;

namespace BGCS.Platforms.Assets;

/// <summary>Resolves the declared native output name and managed import identity before source emission.</summary>
public static class NativeOutputLayout
{
    /// <summary>Resolves a portable native output filename from the requested output form and target facts.</summary>
    /// <param name="libraryName">The configured portable native library stem.</param>
    /// <param name="target">The resolved native platform and ABI.</param>
    /// <param name="kind">The actual output form.</param>
    /// <returns>The exact filename used by construction, publication and consumption.</returns>
    public static string ResolveFilename(
        string libraryName,
        NativeTargetDescriptor target,
        NativeOutputKind kind
    ) {
        ArgumentNullException.ThrowIfNull(target);
        ValidateIdentity(libraryName);
        return kind switch
        {
            NativeOutputKind.StaticLibrary => target.platformId == "windows" ? libraryName + ".lib" : "lib" + libraryName + ".a",
            NativeOutputKind.Object => libraryName + (target.platformId == "windows" ? ".obj" : ".o"),
            NativeOutputKind.SharedLibrary => target.platformId switch
            {
                "windows" => libraryName + ".dll",
                "macos" => "lib" + libraryName + ".dylib",
                "linux" or "android" or "freebsd" => "lib" + libraryName + ".so",
                _ => throw new NotSupportedException("The requested target does not have an implemented dynamic native output layout.")
            },
            _ => throw new NotSupportedException("The native output form is unknown.")
        };
    }

    /// <summary>Resolves the import identity from explicit runtime and native consumption declarations.</summary>
    /// <param name="target">The resolved native platform and ABI.</param>
    /// <param name="runtime">The resolved managed module mechanism.</param>
    /// <param name="configuredNativeName">The library identity used for dynamic imports or a consumer-supplied browser native file.</param>
    /// <param name="nativeArtifactFilename">The exact declared native filename when BGCS builds or stages an artifact; null means the consumer supplies the native file.</param>
    /// <returns>The import identity that generated declarations and native references must share.</returns>
    public static string ResolveImportModuleName(
        NativeTargetDescriptor target,
        ManagedRuntimeProfile runtime,
        string configuredNativeName,
        string? nativeArtifactFilename = null
    ) {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredNativeName);
        if (runtime.kind == ManagedRuntimeKind.BrowserWasm)
        {
            if (target.platformId != "emscripten" || target.architectureId != "wasm32" || target.abiId != "emscripten"
                || runtime.moduleMechanism != NativeModuleMechanism.StaticAdapter)
                throw new NotSupportedException("Browser native imports require the implemented Emscripten wasm32 static module mechanism.");
            if (nativeArtifactFilename is null)
            {
                ValidateIdentity(configuredNativeName);
                return configuredNativeName;
            }
            if (nativeArtifactFilename != Path.GetFileName(nativeArtifactFilename) || nativeArtifactFilename.Contains('/') || nativeArtifactFilename.Contains('\\'))
                throw new ArgumentException("The declared browser native artifact must provide an exact filename without a directory.", nameof(nativeArtifactFilename));
            // .NET's native dependency build registers NativeFileReference.FileName as the P/Invoke module.
            string identity = Path.GetFileNameWithoutExtension(nativeArtifactFilename);
            ValidateIdentity(identity);
            return identity;
        }
        if (runtime.moduleMechanism == NativeModuleMechanism.StaticAdapter)
        {
            if (runtime.kind != ManagedRuntimeKind.NativeAot || target.platformId is not ("windows" or "linux" or "macos" or "freebsd" or "ios" or "ios-simulator"))
                throw new NotSupportedException("This static native module mechanism has no implemented managed import identity.");
            return "__Internal";
        }
        return configuredNativeName;
    }

    private static void ValidateIdentity(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '-' and not '_'))
            throw new ArgumentException("The native library identity must be a portable filename stem.");
    }
}
