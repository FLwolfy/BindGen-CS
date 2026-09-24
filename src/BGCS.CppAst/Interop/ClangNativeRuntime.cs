using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using ClangSharp.Interop;

namespace BGCS.CppAst.Interop;

/// <summary>
/// Loads the bundled libclang before ClangSharp loads its native companion library.
/// </summary>
/// <remarks>
/// On Linux, <c>libClangSharp.so</c> links against libclang's versioned SONAME while
/// the NuGet runtime asset is intentionally distributed as <c>libclang.so</c>.
/// Loading the asset first makes that SONAME available to the dynamic loader and
/// avoids depending on a machine-wide LLVM installation or <c>LD_PRELOAD</c>.
/// </remarks>
internal static class ClangNativeRuntime
{
    private static readonly Lazy<nint> Handle = new(Load, isThreadSafe: true);

    public static void EnsureLoaded()
    {
        _ = Handle.Value;
    }

    private static nint Load()
    {
        string fileName = OperatingSystem.IsWindows()
            ? "libclang.dll"
            : OperatingSystem.IsMacOS()
                ? "libclang.dylib"
                : "libclang.so";

        string runtimeAsset = Path.Combine(
            AppContext.BaseDirectory,
            "runtimes",
            RuntimeInformation.RuntimeIdentifier,
            "native",
            fileName);

        string? configuredDirectory = Environment.GetEnvironmentVariable("BGCS_CLANG_RUNTIME_DIR");
        if (!string.IsNullOrWhiteSpace(configuredDirectory))
        {
            configuredDirectory = Path.GetFullPath(configuredDirectory);
            string clangPath = Path.Combine(configuredDirectory, fileName);
            string companionPath = Path.Combine(configuredDirectory, OperatingSystem.IsWindows()
                ? "libClangSharp.dll"
                : OperatingSystem.IsMacOS() ? "libClangSharp.dylib" : "libClangSharp.so");
            if (!File.Exists(clangPath) || !File.Exists(companionPath))
                throw new DllNotFoundException(
                    $"BGCS_CLANG_RUNTIME_DIR must contain both '{fileName}' and '{Path.GetFileName(companionPath)}': '{configuredDirectory}'.");
            nint configuredClang = NativeLibrary.Load(clangPath);
            nint configuredCompanion = NativeLibrary.Load(companionPath);
            clang.ResolveLibrary += (name, _, _) => name switch
            {
                "libclang" => configuredClang,
                "libClangSharp" => configuredCompanion,
                _ => 0
            };
            return configuredClang;
        }

        if (File.Exists(runtimeAsset) && NativeLibrary.TryLoad(runtimeAsset, out nint handle))
        {
            return handle;
        }

        Assembly assembly = typeof(ClangNativeRuntime).Assembly;
        if (NativeLibrary.TryLoad(fileName, assembly, DllImportSearchPath.SafeDirectories, out handle) ||
            NativeLibrary.TryLoad("libclang", assembly, DllImportSearchPath.SafeDirectories, out handle))
        {
            return handle;
        }

        throw new DllNotFoundException(
            $"Unable to load libclang for runtime '{RuntimeInformation.RuntimeIdentifier}'. " +
            $"Expected a bundled runtime asset at '{runtimeAsset}' or a system libclang installation.");
    }
}
