using System.Collections.Generic;
using System.IO;

namespace BGCS.Runtime;

using System;
using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

#if ANDROID
using Android.Content.PM;
#endif

/// <summary>
/// Flags describing supported target platforms for native library loading.
/// </summary>
public enum TargetPlatform
{
    Unknown = 0,
    FreeBSD = 1 << 0,
    Linux = 1 << 1,
    OSX = 1 << 2,
    Windows = 1 << 3,
    Android = 1 << 4,
    IOS = 1 << 5,
    Tizen = 1 << 6,
    ChromeOS = 1 << 7,
    WebAssembly = 1 << 8,
    Solaris = 1 << 9,
    WatchOS = 1 << 10,
    TVO = 1 << 11,
    Any = FreeBSD | Linux | OSX | Windows | Android | IOS | Tizen | ChromeOS | WebAssembly | Solaris | WatchOS | TVO
}

/// <summary>
/// Callback that resolves a concrete file path for a library name.
/// </summary>
/// <param name="libraryName">Requested library name.</param>
/// <param name="pathToLibrary">Resolved file path when handled.</param>
/// <returns><see langword="true"/> when handled; otherwise <see langword="false"/>.</returns>
public delegate bool ResolvePathHandler(string libraryName, out string? pathToLibrary);

/// <summary>
/// Callback that can rewrite a library name before loading.
/// </summary>
/// <param name="libraryName">Library name to inspect or replace.</param>
/// <returns><see langword="true"/> to stop interceptor chain; otherwise <see langword="false"/>.</returns>
public delegate bool LibraryNameInterceptor(ref string libraryName);

/// <summary>
/// Callback that can provide a library handle directly, bypassing default loading.
/// </summary>
/// <param name="libraryName">Library name being loaded.</param>
/// <param name="pointer">Native library handle when handled.</param>
/// <returns><see langword="true"/> when handled; otherwise <see langword="false"/>.</returns>
public delegate bool LibraryLoadInterceptor(string libraryName, out nint pointer);

/// <summary>
/// Callback that can provide a custom <see cref="INativeContext"/> implementation.
/// </summary>
/// <param name="libraryName">Library name being loaded.</param>
/// <param name="context">Resolved native context when handled.</param>
/// <returns><see langword="true"/> when handled; otherwise <see langword="false"/>.</returns>
public delegate bool NativeContextInterceptor(string libraryName, out INativeContext? context);

/// <summary>
/// Platform-aware native library loading pipeline with interception hooks.
/// </summary>
public static class LibraryLoader
{
        private static readonly EventHandlerList<ResolvePathHandler> resolvePathHandlers = [];
        private static readonly EventHandlerList<LibraryNameInterceptor> libraryNameInterceptors = [];
        private static readonly EventHandlerList<LibraryLoadInterceptor> libraryLoadInterceptors = [];
        private static readonly EventHandlerList<NativeContextInterceptor> nativeContextInterceptors = [];

        /// <summary>Custom <see cref="OSPlatform"/> marker for FreeBSD.</summary>
        public static OSPlatform FreeBSD { get; } = OSPlatform.Create("FREEBSD");

        /// <summary>Custom <see cref="OSPlatform"/> marker for Linux.</summary>
        public static OSPlatform Linux { get; } = OSPlatform.Create("LINUX");

        /// <summary>Custom <see cref="OSPlatform"/> marker for macOS.</summary>
        public static OSPlatform OSX { get; } = OSPlatform.Create("OSX");

        /// <summary>Custom <see cref="OSPlatform"/> marker for Windows.</summary>
        public static OSPlatform Windows { get; } = OSPlatform.Create("WINDOWS");

        /// <summary>Custom <see cref="OSPlatform"/> marker for Android.</summary>
        public static OSPlatform Android { get; } = OSPlatform.Create("ANDROID");

        /// <summary>Custom <see cref="OSPlatform"/> marker for iOS.</summary>
        public static OSPlatform IOS { get; } = OSPlatform.Create("IOS");

        /// <summary>Custom <see cref="OSPlatform"/> marker for Tizen.</summary>
        public static OSPlatform Tizen { get; } = OSPlatform.Create("TIZEN");

        /// <summary>Custom <see cref="OSPlatform"/> marker for ChromeOS.</summary>
        public static OSPlatform ChromeOS { get; } = OSPlatform.Create("CHROMEOS");

        /// <summary>Custom <see cref="OSPlatform"/> marker for WebAssembly.</summary>
        public static OSPlatform WebAssembly { get; } = OSPlatform.Create("WEBASSEMBLY");

        /// <summary>Custom <see cref="OSPlatform"/> marker for Solaris.</summary>
        public static OSPlatform Solaris { get; } = OSPlatform.Create("SOLARIS");

        /// <summary>Custom <see cref="OSPlatform"/> marker for watchOS.</summary>
        public static OSPlatform WatchOS { get; } = OSPlatform.Create("WATCHOS");

        /// <summary>Custom <see cref="OSPlatform"/> marker for tvOS.</summary>
        public static OSPlatform TVOS { get; } = OSPlatform.Create("TVOS");

        /// <summary>
        /// Additional probing folders for native libraries.
        /// Relative paths are resolved from <see cref="AppContext.BaseDirectory"/>.
        /// </summary>
        public static List<string> CustomLoadFolders { get; } = [];

        /// <summary>Occurs when the loader needs to resolve a concrete library file path.</summary>
        public static event ResolvePathHandler ResolvePath { add => resolvePathHandlers.Add(value); remove => resolvePathHandlers.Remove(value); }

        /// <summary>Occurs before loading to allow rewriting the library name.</summary>
        public static event LibraryNameInterceptor InterceptLibraryName { add => libraryNameInterceptors.Add(value); remove => libraryNameInterceptors.Remove(value); }

        /// <summary>Occurs before default loading to allow providing a native handle directly.</summary>
        public static event LibraryLoadInterceptor InterceptLibraryLoad { add => libraryLoadInterceptors.Add(value); remove => libraryLoadInterceptors.Remove(value); }

        /// <summary>Occurs before default loading to allow providing a custom native context.</summary>
        public static event NativeContextInterceptor InterceptNativeContext { add => nativeContextInterceptors.Add(value); remove => nativeContextInterceptors.Remove(value); }

        /// <summary>
        /// Gets the default library file extension for the current platform.
        /// </summary>
        /// <returns>Platform-specific extension such as <c>.dll</c>, <c>.so</c> or <c>.dylib</c>.</returns>
        public static string GetExtension()
        {
            // Default extension based on platform
            if (RuntimeInformation.IsOSPlatform(Windows))
            {
                return ".dll";
            }
            else if (RuntimeInformation.IsOSPlatform(OSX))
            {
                return ".dylib";
            }
            else if (RuntimeInformation.IsOSPlatform(Linux))
            {
                return ".so";
            }
            else if (RuntimeInformation.IsOSPlatform(Android))
            {
                return ".so";
            }
            else if (RuntimeInformation.IsOSPlatform(IOS))
            {
                return ".dylib"; // iOS also uses .dylib for dynamic libraries
            }
            else if (RuntimeInformation.IsOSPlatform(FreeBSD))
            {
                return ".so";
            }
            else if (RuntimeInformation.IsOSPlatform(TVOS))
            {
                return ".dylib"; // tvOS uses the same dynamic library extension as iOS
            }
            else if (RuntimeInformation.IsOSPlatform(WatchOS))
            {
                return ".dylib"; // watchOS also uses .dylib
            }
            else if (RuntimeInformation.IsOSPlatform(Solaris))
            {
                return ".so";
            }
            else if (RuntimeInformation.IsOSPlatform(WebAssembly))
            {
                return ".wasm";
            }
            else if (RuntimeInformation.IsOSPlatform(Tizen))
            {
                return ".so";
            }
            else if (RuntimeInformation.IsOSPlatform(ChromeOS))
            {
                return ".so";
            }

            // Default to .so if no platform matches
            return ".so";
        }

        /// <summary>
        /// Callback used to provide the logical native library name.
        /// </summary>
        public delegate string LibraryNameCallback();

        /// <summary>
        /// Callback used to provide a custom native library extension.
        /// </summary>
        public delegate string LibraryExtensionCallback();

        /// <summary>
        /// Registers an interceptor that binds a library name to the current process main module.
        /// </summary>
        /// <param name="targetLibraryName">Logical library name to intercept.</param>
        public static void LoadFromMainModule(string targetLibraryName)
        {
            LoadFrom(targetLibraryName, Process.GetCurrentProcess().MainModule!.BaseAddress);
        }

        /// <summary>
        /// Registers an interceptor that binds a library name to an existing module handle.
        /// </summary>
        /// <param name="targetLibraryName">Logical library name to intercept.</param>
        /// <param name="address">Native module handle to return for that name.</param>
        public static void LoadFrom(string targetLibraryName, nint address)
        {
            bool Callback(string libraryName, out nint pointer)
            {
                if (libraryName == targetLibraryName)
                {
                    pointer = address;
                    return true;
                }

                pointer = 0;
                return false;
            }

            libraryLoadInterceptors.Add(Callback);
        }

        /// <summary>
        /// Loads a native library and returns a ready-to-use <see cref="INativeContext"/>.
        /// </summary>
        /// <param name="libraryNameCallback">Callback providing logical library name.</param>
        /// <param name="libraryExtensionCallback">Optional callback overriding extension resolution.</param>
        /// <returns>Native context used by generated bindings.</returns>
        public static INativeContext LoadLibraryEx(LibraryNameCallback libraryNameCallback, LibraryExtensionCallback? libraryExtensionCallback)
        {
            var libraryName = libraryNameCallback();

            foreach (var callback in nativeContextInterceptors)
            {
                if (callback(libraryName, out var context))
                {
                    return context ?? throw new InvalidOperationException("'context' cannot be null when returning true.");
                }
            }

            return new NativeLibraryContext(LoadLibrary(libraryNameCallback, libraryExtensionCallback));
        }

        /// <summary>
        /// Loads a native library and returns its native module handle.
        /// </summary>
        /// <param name="libraryNameCallback">Callback providing logical library name.</param>
        /// <param name="libraryExtensionCallback">Optional callback overriding extension resolution.</param>
        /// <returns>Native module handle.</returns>
        /// <exception cref="DllNotFoundException">Thrown when no candidate library can be loaded.</exception>
        public static nint LoadLibrary(LibraryNameCallback libraryNameCallback, LibraryExtensionCallback? libraryExtensionCallback)
        {
            var libraryName = libraryNameCallback();

            foreach (var callback in libraryNameInterceptors)
            {
                if (callback(ref libraryName))
                {
                    break;
                }
            }

            foreach (var callback in libraryLoadInterceptors)
            {
                if (callback(libraryName, out nint pointer))
                {
                    return pointer;
                }
            }

            var extension = libraryExtensionCallback != null ? libraryExtensionCallback() : GetExtension();

            if (!libraryName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                libraryName += extension;
            }

            var osPlatform = GetOSPlatform();
            var architecture = GetArchitecture();
            var libraryPath = GetNativeAssemblyPath(osPlatform, architecture, libraryName);

            nint handle = NativeLibrary.Load(libraryPath);

            if (handle == IntPtr.Zero)
            {
                throw new DllNotFoundException($"Unable to load library '{libraryName}'.");
            }

            return handle;
        }

        private static string GetNativeAssemblyPath(string osPlatform, string architecture, string libraryName)
        {
            foreach (var callback in resolvePathHandlers)
            {
                if (callback(libraryName, out var pathToLibrary))
                {
                    return pathToLibrary ?? throw new InvalidOperationException("'pathToLibrary' cannot be null when returning true.");
                }
            }

#if ANDROID
            // Get the application info
            ApplicationInfo appInfo = Application.Context.ApplicationInfo!;

            // Get the native library directory path
            string assemblyLocation = appInfo.NativeLibraryDir!;

#else
            string assemblyLocation = AppContext.BaseDirectory;
#endif

            List<string> paths =
            [
                 Path.Combine(assemblyLocation, libraryName),
                 Path.Combine(assemblyLocation, "runtimes", osPlatform, "native", libraryName),
                 Path.Combine(assemblyLocation, "runtimes", $"{osPlatform}-{architecture}", "debug", libraryName), // allows debug builds sideload.
                 Path.Combine(assemblyLocation, "runtimes", $"{osPlatform}-{architecture}", "native", libraryName),
            ];

            foreach (var customPath in CustomLoadFolders)
            {
                if (IsPathFullyQualified(customPath))
                {
                    paths.Add(Path.Combine(customPath, libraryName));
                }
                else
                {
                    paths.Add(Path.Combine(assemblyLocation, customPath, libraryName));
                }
            }

            foreach (var path in paths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return libraryName;
        }

        /// <summary>
        /// Determines whether a path is absolute/fully qualified for the current platform.
        /// </summary>
        /// <param name="path">Path to test.</param>
        /// <returns><see langword="true"/> when absolute; otherwise <see langword="false"/>.</returns>
        public static bool IsPathFullyQualified(string path)
        {
            if (path.Length == 0)
            {
                return false;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return IsFullyQualifiedWindows(path);
            }

            return IsFullyQualifiedUnix(path);
        }

        private static bool IsFullyQualifiedWindows(string path)
        {
            if (path.Length < 2)
            {
                return false;
            }

            if (char.IsLetter(path[0]) && path[1] == ':' &&
                (path.Length > 2 && (path[2] == '\\' || path[2] == '/')))
            {
                return true;
            }

            if (path.Length > 1 && path[0] == '\\' && path[1] == '\\')
            {
                return true;
            }

            return false;
        }

        private static bool IsFullyQualifiedUnix(string path)
        {
            return path.Length > 0 && path[0] == '/';
        }

        private static string GetArchitecture()
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X86 => "x86",
                Architecture.X64 => "x64",
                Architecture.Arm => "arm",
                Architecture.Arm64 => "arm64",
                _ => throw new ArgumentException("Unsupported architecture."),
            };
        }

        private static string GetOSPlatform()
        {
            if (RuntimeInformation.IsOSPlatform(Windows))
            {
                return "win";
            }
            else if (RuntimeInformation.IsOSPlatform(Linux))
            {
                return "linux";
            }
            else if (RuntimeInformation.IsOSPlatform(OSX))
            {
                return "osx";
            }
            else if (RuntimeInformation.IsOSPlatform(Android))
            {
                return "android";
            }
            else if (RuntimeInformation.IsOSPlatform(IOS))
            {
                return "ios";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Create("FREEBSD")))
            {
                return "freebsd";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Create("TVOS")))
            {
                return "tvos";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Create("WATCHOS")))
            {
                return "watchos";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Create("SOLARIS")))
            {
                return "solaris";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Create("WEBASSEMBLY")))
            {
                return "webassembly";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Create("TIZEN")))
            {
                return "tizen";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Create("CHROMEOS")))
            {
                return "chromeos";
            }

            throw new ArgumentException("Unsupported OS platform.");
        }
}
