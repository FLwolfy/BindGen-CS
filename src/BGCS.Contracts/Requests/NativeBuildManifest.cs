using System.Collections.Immutable;
using System.Text.Json;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Targets;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Native;
using BGCS.Contracts.Plugins;

namespace BGCS.Contracts.Requests;

/// <summary>Declares one locked build source using an explicit artifact or external path origin.</summary>
/// <param name="path">An artifact:/ portable artifact path, or an absolute external input path.</param>
/// <param name="language">The source language.</param>
/// <param name="standard">The exact language standard.</param>
public sealed record NativeManifestCompilationUnit(
    string path,
    SourceLanguage language,
    string standard
);

/// <summary>Freezes complete native construction inputs independently of the frontend, CLI and mutable configuration.</summary>
/// <param name="libraryId">The stable publication owner.</param>
/// <param name="libraryName">The native output filename stem.</param>
/// <param name="target">The concrete ABI and pinned compiler/SDK facts.</param>
/// <param name="sources">All upstream, bridge and storage-assertion compilation inputs.</param>
/// <param name="artifacts">Content locks for every generated native input.</param>
/// <param name="externalInputs">Content locks for all declared external inputs known at generation.</param>
/// <param name="requiredExports">Exact final ABI linker symbols.</param>
public sealed record NativeBuildManifest(
    string libraryId,
    string libraryName,
    NativeTargetDescriptor target,
    ImmutableArray<NativeManifestCompilationUnit> sources,
    ImmutableArray<ArtifactDescriptor> artifacts,
    ImmutableArray<InputDependency> externalInputs,
    ImmutableArray<string> requiredExports
)
{
    /// <summary>Gets the only accepted complete manifest schema revision.</summary>
    public int schemaVersion { get; init; } = 1;
    /// <summary>Gets the generating product major version.</summary>
    public int contractsMajor { get; init; } = BGCS.Contracts.Versioning.ContractVersion.C_MAJOR;
    /// <summary>Gets the artifact root relative to the declaring manifest directory.</summary>
    public string artifactRoot { get; init; } = ".";
    /// <summary>Gets the requested compiler or build-system provider.</summary>
    public string providerId { get; init; } = "auto";
    /// <summary>Gets the exact native construction form.</summary>
    public NativeOutputKind outputKind { get; init; } = NativeOutputKind.SharedLibrary;
    /// <summary>Gets the resolved generated-binding runtime profile, paired with its import identity and native filename; null denotes a native-only manifest.</summary>
    public ManagedRuntimeProfile? managedRuntime { get; init; }
    /// <summary>Gets the generated declarations' exact managed import identity; native output overrides must preserve browser registration.</summary>
    public string? importModuleName { get; init; }
    /// <summary>Gets the exact portable native output filename frozen for the generated import contract.</summary>
    public string? nativeArtifactFilename { get; init; }
    /// <summary>Gets ordered normal header paths with explicit artifact:/ or absolute external origins.</summary>
    public ImmutableArray<string> includeDirectories { get; init; } = [];
    /// <summary>Gets ordered system header paths with explicit artifact:/ or absolute external origins.</summary>
    public ImmutableArray<string> systemIncludeDirectories { get; init; } = [];
    /// <summary>Gets native macro definitions.</summary>
    public ImmutableArray<string> defines { get; init; } = [];
    /// <summary>Gets individual native compiler arguments.</summary>
    public ImmutableArray<string> compilerArguments { get; init; } = [];
    /// <summary>Gets individual native linker arguments.</summary>
    public ImmutableArray<string> linkerArguments { get; init; } = [];
    /// <summary>Gets absolute dependency library paths or linker library names.</summary>
    public ImmutableArray<string> libraries { get; init; } = [];
    /// <summary>Gets ordered absolute library search roots.</summary>
    public ImmutableArray<string> libraryDirectories { get; init; } = [];
    /// <summary>Gets the explicit optional native build-system executable.</summary>
    public string? buildToolPath { get; init; }
    /// <summary>Gets the explicit optional auxiliary build tool executable.</summary>
    public string? auxiliaryToolPath { get; init; }
    /// <summary>Gets original frozen plugin activations and explicit factory-only host requirements.</summary>
    public ImmutableArray<NativeManifestPluginActivation> plugins { get; init; } = [];
    /// <summary>Gets locked parser-specific extensions required for independent final-effective native fact verification.</summary>
    public ImmutableArray<WorkerPluginActivation> workerPlugins { get; init; } = [];
    /// <summary>Gets the complete frozen configuration/target context used to recapture worker extension inputs before verification.</summary>
    public CacheFingerprintContext? workerPluginContext { get; init; }
    /// <summary>Gets cloned generation settings used by extension input and fingerprint providers.</summary>
    public JsonElement? generationConfiguration { get; init; }
    /// <summary>Gets the original absolute generation input base retained for extension dependencies.</summary>
    public string? generationPathBase { get; init; }
    /// <summary>Gets the final native ABI used by configured validation gates, or null for a standalone handwritten native manifest.</summary>
    public InteropModule? interop { get; init; }
    /// <summary>Gets the selected source facts used by generated declarations, paired with compiler verification source inputs.</summary>
    public NativeModule? sourceFacts { get; init; }
    /// <summary>Gets the exact source frontend body-retention option; required with source facts and absent for native-only manifests.</summary>
    public bool? sourceRetainFunctionBodies { get; init; }
    /// <summary>Gets content-locked inputs preprocessed with matching native compilation flags without adding compiled translation units.</summary>
    public ImmutableArray<NativeManifestCompilationUnit> preprocessingSources { get; init; } = [];
    /// <summary>Gets exact artifact:/ preprocessing inputs for independent final-effective constructor, record and bitfield verification.</summary>
    public ImmutableArray<string> factVerificationSources { get; init; } = [];
    /// <summary>Gets complete file, search-directory and absence locks for extension inputs that affected the generated ABI.</summary>
    public ImmutableArray<SourceSnapshot> extensionInputs { get; init; } = [];
    /// <summary>Gets exact portable membership and content locks for generated inputs relocated from verified private stores.</summary>
    public ImmutableArray<NativeGeneratedDirectoryContract> generatedDirectories { get; init; } = [];
}
