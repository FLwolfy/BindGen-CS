using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

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
