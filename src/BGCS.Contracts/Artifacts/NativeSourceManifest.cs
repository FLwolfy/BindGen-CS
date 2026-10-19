using System.Collections.Immutable;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Requests;

namespace BGCS.Contracts.Artifacts;

/// <summary>Describes generated native source requirements without a build or execution plan.</summary>
/// <param name="schemaVersion">The native source manifest revision.</param>
/// <param name="targetId">The concrete native target.</param>
/// <param name="triple">The parser target triple.</param>
/// <param name="language">The original declaration language.</param>
/// <param name="languageStandard">The source language standard.</param>
/// <param name="sources">Generated translation units, relative to the publication root.</param>
/// <param name="headers">Generated headers, relative to the publication root.</param>
/// <param name="inputRoots">Portable input root identities; physical bindings are returned separately.</param>
/// <param name="inputs">Parsed and explicitly declared input files with exact content identities.</param>
/// <param name="includeDirectories">Ordered consumer header search requirements.</param>
/// <param name="headerReferences">The resolved includes emitted for original entry headers.</param>
/// <param name="defines">Source and target preprocessor definitions.</param>
/// <param name="requiredExports">Expected native exports, without a linker command.</param>
/// <param name="languageRequirements">Additional native language requirements established by lowering.</param>
public sealed record NativeSourceManifest(
    int schemaVersion,
    string targetId,
    string triple,
    SourceLanguage language,
    string languageStandard,
    ImmutableArray<ArtifactDescriptor> sources,
    ImmutableArray<ArtifactDescriptor> headers,
    ImmutableArray<NativeInputRoot> inputRoots,
    ImmutableArray<NativeInputFile> inputs,
    ImmutableArray<NativeIncludeDirectory> includeDirectories,
    ImmutableArray<NativeHeaderReference> headerReferences,
    ImmutableArray<string> defines,
    ImmutableArray<string> requiredExports,
    ImmutableArray<CppLanguageRequirement> languageRequirements
)
{
    /// <summary>Gets the optional generated static function table protocol.</summary>
    public StaticExportTableDescriptor? staticExportTable { get; init; }
}

/// <summary>Identifies a consumer-remappable source, SDK or generated-artifact root.</summary>
/// <param name="id">A stable identity within this target manifest.</param>
/// <param name="kind">One of project, user, sdk, sdk-root or generated; generated is the publication root.</param>
public sealed record NativeInputRoot(
    string id,
    string kind
);

/// <summary>Binds an input root to this session's physical location; never embedded in portable source manifests.</summary>
/// <param name="targetId">The manifest target.</param>
/// <param name="rootId">The portable input root identity.</param>
/// <param name="directory">The absolute input directory, or formal output for generated artifacts.</param>
public sealed record NativeInputRootBinding(
    string targetId,
    string rootId,
    string directory
);

/// <summary>Identifies exact input bytes independently of a workspace location.</summary>
/// <param name="rootId">The input root identity.</param>
/// <param name="relativePath">The canonical root-relative filename.</param>
/// <param name="length">The captured byte length.</param>
/// <param name="digest">The captured content digest.</param>
/// <param name="role">EntryHeader, Dependency or ShimSource; only ShimSource requests a consumer translation unit.</param>
public sealed record NativeInputFile(
    string rootId,
    string relativePath,
    long length,
    ContentDigest digest,
    string role
);

/// <summary>Describes a consumer include search location in declared order.</summary>
/// <param name="rootId">The input root identity.</param>
/// <param name="relativePath">A root-relative directory, empty for the root itself.</param>
/// <param name="isSystem">Whether the directory supplies target system declarations.</param>
/// <param name="isFramework">Whether the directory uses Apple framework search semantics.</param>
public sealed record NativeIncludeDirectory(
    string rootId,
    string relativePath,
    bool isSystem,
    bool isFramework
);

/// <summary>Associates an emitted include spelling with its verified original input identity.</summary>
/// <param name="rootId">The original input root identity.</param>
/// <param name="relativePath">The root-relative input filename.</param>
/// <param name="include">The portable include spelling; generated inputs use artifact-relative references instead.</param>
public sealed record NativeHeaderReference(
    string rootId,
    string relativePath,
    string include
);
