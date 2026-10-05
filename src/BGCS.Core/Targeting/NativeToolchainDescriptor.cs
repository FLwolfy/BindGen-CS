using System;
using System.Collections.Generic;
using System.Linq;

namespace BGCS.Core.Targeting;

/// <summary>
/// Contains immutable SDK and compiler inputs separately from compiler builtin resources.
/// </summary>
public sealed class NativeToolchainDescriptor
{
    /// <summary>
    /// Copies toolchain inputs without requiring the compiler to run on the generator host.
    /// </summary>
    /// <param name = "compilerPath">
    /// The compiler driver path or executable name, or null for provider discovery.
    /// </param>
    /// <param name = "sysRoot">
    /// The target SDK root, or null when no SDK is supplied.
    /// </param>
    /// <param name = "systemIncludeFolders">
    /// Target SDK include directories; null represents an empty set.
    /// </param>
    /// <param name = "defines">
    /// Target compiler definitions; null represents an empty set.
    /// </param>
    /// <param name = "arguments">
    /// Target compiler arguments; null represents an empty set.
    /// </param>
    /// <param name="cxxSystemIncludeFolders">
    /// Target C++ standard library directories, excluded from C parsing; null represents an empty set.
    /// </param>
    public NativeToolchainDescriptor(
        string? compilerPath = null,
        string? sysRoot = null,
        IEnumerable<string>? systemIncludeFolders = null,
        IEnumerable<string>? defines = null,
        IEnumerable<string>? arguments = null,
        IEnumerable<string>? cxxSystemIncludeFolders = null
    ) {
        this.compilerPath = compilerPath;
        this.sysRoot = sysRoot;
        this.systemIncludeFolders = Copy(systemIncludeFolders);
        this.defines = Copy(defines);
        this.arguments = Copy(arguments);
        this.cxxSystemIncludeFolders = Copy(cxxSystemIncludeFolders);
    }

    /// <summary>
    /// Gets the compiler driver identity supplied by the caller or target provider.
    /// </summary>
    public string? compilerPath { get; }
    /// <summary>
    /// Gets the target SDK root, independently of libclang's builtin header directory.
    /// </summary>
    public string? sysRoot { get; }
    /// <summary>
    /// Gets the ordered target SDK include paths.
    /// </summary>
    public IReadOnlyList<string> systemIncludeFolders { get; }
    /// <summary>
    /// Gets C++ standard library paths supplied by the selected target SDK or caller.
    /// </summary>
    public IReadOnlyList<string> cxxSystemIncludeFolders { get; }
    /// <summary>
    /// Gets the target definitions applied before parsing.
    /// </summary>
    public IReadOnlyList<string> defines { get; }
    /// <summary>
    /// Gets the target compiler arguments applied before parsing.
    /// </summary>
    public IReadOnlyList<string> arguments { get; }

    private static IReadOnlyList<string> Copy(IEnumerable<string>? values)
    {
        string[] copy = (values ?? []).ToArray();
        foreach (string value in copy)
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return Array.AsReadOnly(copy);
    }
}
