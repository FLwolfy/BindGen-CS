using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using ClangSharp.Interop;

namespace BGCS.CppAst.Interop;

/// <summary>
/// Owns the parser's matching libclang and ClangSharp native companions for the process lifetime.
/// </summary>
/// <remarks>
/// Native assets are resolved relative to the parser assembly as well as the executable. This supports
/// library hosts such as MSBuild whose executable directory differs from the library deployment.
/// Loading libclang first also satisfies its versioned SONAME dependency on Unix systems.
/// </remarks>
internal static class ClangNativeRuntime
{
    private static readonly Lazy<nint> m_handle = new(Load, isThreadSafe: true);

    internal static void EnsureLoaded() => _ = m_handle.Value;

    private static nint Load()
    {
        string clangName = GetLibraryName("libclang");
        string companionName = GetLibraryName("libClangSharp");
        string? configuredDirectory = Environment.GetEnvironmentVariable("BGCS_CLANG_RUNTIME_DIR");
        if (!string.IsNullOrWhiteSpace(configuredDirectory))
            return LoadPair(Path.GetFullPath(configuredDirectory), clangName, companionName);

        Assembly parser = typeof(ClangNativeRuntime).Assembly;
        string[] roots = new[]
        {
            Path.GetDirectoryName(parser.Location),
            Path.GetDirectoryName(typeof(clang).Assembly.Location),
            AppContext.BaseDirectory
        }.OfType<string>().Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToArray();
        foreach (string root in roots)
        {
            string directory = Path.Combine(root, "runtimes", RuntimeInformation.RuntimeIdentifier, "native");
            if (File.Exists(Path.Combine(directory, clangName)) || File.Exists(Path.Combine(directory, companionName)))
                return LoadPair(directory, clangName, companionName);
            if (File.Exists(Path.Combine(root, clangName)) || File.Exists(Path.Combine(root, companionName)))
                return LoadPair(root, clangName, companionName);
        }

        nint handle = NativeLibrary.Load(clangName, parser, DllImportSearchPath.SafeDirectories);
        nint companion;
        try
        {
            companion = NativeLibrary.Load(companionName, typeof(clang).Assembly, DllImportSearchPath.SafeDirectories);
        }
        catch
        {
            NativeLibrary.Free(handle);
            throw;
        }
        Register(handle, companion);
        return handle;
    }

    private static nint LoadPair(
        string directory,
        string clangName,
        string companionName
    ) {
        string clangPath = Path.Combine(directory, clangName);
        string companionPath = Path.Combine(directory, companionName);
        if (!File.Exists(clangPath) || !File.Exists(companionPath))
            throw new DllNotFoundException($"The parser runtime directory must contain both '{clangName}' and '{companionName}': '{directory}'.");
        nint handle = NativeLibrary.Load(clangPath);
        nint companion;
        try
        {
            companion = NativeLibrary.Load(companionPath);
        }
        catch
        {
            NativeLibrary.Free(handle);
            throw;
        }
        Register(handle, companion);
        return handle;
    }

    private static void Register(
        nint handle,
        nint companion
    ) {
        clang.ResolveLibrary += (
            name,
            _,
            _
        ) => name switch
        {
            "libclang" => handle,
            "libClangSharp" => companion,
            _ => 0
        };
    }

    private static string GetLibraryName(string name)
        => name + (OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so");
}
