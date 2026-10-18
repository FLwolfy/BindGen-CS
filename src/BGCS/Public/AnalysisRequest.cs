using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Targets;

namespace BGCS;

/// <summary>Declares immutable native-analysis inputs before any frontend process starts.</summary>
/// <param name="headers">The ordered source entry headers.</param>
/// <param name="language">The source language.</param>
/// <param name="baseDirectory">The captured base for relative paths.</param>
public sealed record AnalysisRequest(
    ImmutableArray<string> headers,
    SourceLanguage language,
    string baseDirectory
)
{
    /// <summary>Gets the requested target identity; host is resolved to a concrete target per session.</summary>
    public string targetId { get; init; } = "host";
    /// <summary>Gets an explicit compiler triple constrained by the requested native target; null uses its provider default.</summary>
    public string? triple { get; init; }
    /// <summary>Gets the explicitly declared target SDK and compiler inputs.</summary>
    public NativeToolchainDescriptor toolchain { get; init; } = new();
    /// <summary>Gets the C or C++ language standard.</summary>
    public string languageStandard { get; init; } = language == SourceLanguage.Cpp ? "c++23" : "c17";
    /// <summary>Gets user header search roots in precedence order.</summary>
    public ImmutableArray<string> includeDirectories { get; init; } = [];
    /// <summary>Gets target system header search roots.</summary>
    public ImmutableArray<string> systemIncludeDirectories { get; init; } = [];
    /// <summary>Gets preprocessor definitions.</summary>
    public ImmutableArray<string> defines { get; init; } = [];
    /// <summary>Gets explicit frontend arguments.</summary>
    public ImmutableArray<string> arguments { get; init; } = [];
    /// <summary>Gets source text placed before entry headers.</summary>
    public string preamble { get; init; } = string.Empty;
    /// <summary>Gets source text placed after entry headers.</summary>
    public string postamble { get; init; } = string.Empty;
    /// <summary>Gets whether source macro facts are retained.</summary>
    public bool parseMacros { get; init; } = true;
    /// <summary>Gets whether source documentation is retained.</summary>
    public bool parseComments { get; init; } = true;
    /// <summary>Gets whether system declarations are retained.</summary>
    public bool parseSystemIncludes { get; init; }
    /// <summary>Gets whether the parser retains function bodies when the requested analysis needs actual definition facts.</summary>
    public bool retainFunctionBodies { get; init; }
    internal ImmutableArray<string> snapshotExcludedDirectories { get; init; } = [];
    internal ImmutableDictionary<string, string> sourcePathMappings { get; init; } = ImmutableDictionary<string, string>.Empty;

    /// <summary>Copies source entries and captures their relative-path base at request creation.</summary>
    /// <param name="headers">Source entry paths.</param>
    /// <param name="language">C by default, or an explicitly selected language.</param>
    /// <param name="baseDirectory">The path base, or the current directory captured now.</param>
    /// <returns>An immutable source analysis request.</returns>
    public static AnalysisRequest FromFiles(
        IEnumerable<string> headers,
        SourceLanguage language = SourceLanguage.C,
        string? baseDirectory = null
    ) {
        ArgumentNullException.ThrowIfNull(headers);
        return new(headers.ToImmutableArray(), language, Path.GetFullPath(baseDirectory ?? Environment.CurrentDirectory));
    }
}
