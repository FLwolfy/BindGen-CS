using System.Collections.Immutable;
using BGCS.Core.Inputs;
using BGCS.Core.Targets;
using BGCS.Core.Plugins;

namespace BGCS.Core.Requests;

/// <summary>Declares resolved parser inputs without attaching discovery or execution services.</summary>
/// <param name="target">The concrete native target and SDK facts.</param>
/// <param name="language">The source language.</param>
/// <param name="headers">Absolute entry header paths in declared order.</param>
public sealed record FrontendRequest(
    NativeTargetDescriptor target,
    SourceLanguage language,
    ImmutableArray<string> headers
)
{
    /// <summary>Gets the explicit parse role; contributors may leave irrelevant verification graphs unchanged.</summary>
    public FrontendRequestPurpose purpose { get; init; } = FrontendRequestPurpose.SourceAnalysis;
    /// <summary>Gets locked parser-specific extensions that may be activated only in the matching worker.</summary>
    public ImmutableArray<WorkerPluginActivation> workerPlugins { get; init; } = [];
    /// <summary>Gets the frozen configuration and resolved target used by worker extension fingerprint providers.</summary>
    public CacheFingerprintContext? workerPluginContext { get; init; }
    /// <summary>Gets preflight extension state independently reproduced by each fresh parse worker.</summary>
    public WorkerPluginCapture? workerPluginCapture { get; init; }
    /// <summary>Gets the source-language standard.</summary>
    public string languageStandard { get; init; } = language == SourceLanguage.Cpp ? "c++23" : "c17";
    /// <summary>Gets explicit user include directories.</summary>
    public ImmutableArray<string> includeDirectories { get; init; } = [];
    /// <summary>Gets explicit target system include directories.</summary>
    public ImmutableArray<string> systemIncludeDirectories { get; init; } = [];
    /// <summary>Gets resolved driver header search order, including the position of parser builtins and framework roots.</summary>
    public ImmutableArray<HeaderSearchPath> headerSearchPaths { get; init; } = [];
    /// <summary>Gets source preprocessor definitions.</summary>
    public ImmutableArray<string> defines { get; init; } = [];
    /// <summary>Gets additional frontend arguments.</summary>
    public ImmutableArray<string> arguments { get; init; } = [];
    /// <summary>Gets a preamble placed before entry header includes.</summary>
    public string preamble { get; init; } = string.Empty;
    /// <summary>Gets explicit declarations placed after entry headers, including template instantiations.</summary>
    public string postamble { get; init; } = string.Empty;
    /// <summary>Gets whether macro facts are collected.</summary>
    public bool parseMacros { get; init; } = true;
    /// <summary>Gets whether native documentation is collected.</summary>
    public bool parseComments { get; init; } = true;
    /// <summary>Gets whether system declarations are retained in the fact graph.</summary>
    public bool parseSystemIncludes { get; init; }
    /// <summary>Gets whether function bodies remain available for compiler-established definition facts; false avoids retaining unneeded C++ bodies.</summary>
    public bool retainFunctionBodies { get; init; }
    /// <summary>Gets the path base used to normalize logical source locations.</summary>
    public string pathBase { get; init; } = string.Empty;
    /// <summary>Gets host-owned private candidates whose output membership must not invalidate source search snapshots.</summary>
    public ImmutableArray<string> snapshotExcludedDirectories { get; init; } = [];
    /// <summary>Gets explicit physical-file to portable logical-source mappings for Host-owned generated inputs; dependency snapshots retain physical paths.</summary>
    public ImmutableDictionary<string, string> sourcePathMappings { get; init; } = ImmutableDictionary<string, string>.Empty;
    /// <summary>Gets whether detached locations honor compiler-produced line directives, while token spans and dependency paths remain physical.</summary>
    public bool usePresumedSourceLocations { get; init; }
}
