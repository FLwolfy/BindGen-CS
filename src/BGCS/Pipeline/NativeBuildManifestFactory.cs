using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BGCS.Configuration;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.Infrastructure.IO;
using BGCS.Platforms.Capabilities;
using BGCS.Composition;
using System.Text.Json;

namespace BGCS;

internal static class NativeBuildManifestFactory
{
    internal static NativeBuildManifest Create(
        BindingConfiguration configuration,
        NativeTargetDescriptor target,
        SourceSnapshot originalInputs,
        ImmutableArray<ArtifactDescriptor> nativeArtifacts,
        string baseDirectory,
        ImmutableArray<string> targetDefines,
        FrozenExtensionRegistry? definition = null,
        NativeGeneratedInputRelocation? generatedInputs = null,
        FrontendRequest? preparedFrontend = null
    ) {
        string prefix = target.targetId.value + "/native/";
        ImmutableArray<ArtifactDescriptor> generated = nativeArtifacts.Where(artifact => artifact.relativePath.StartsWith(prefix, StringComparison.Ordinal)
            && !artifact.relativePath.EndsWith("/bridge.manifest.json", StringComparison.Ordinal)
            || generatedInputs is not null && generatedInputs.contracts.Any(directory => artifact.relativePath.StartsWith(directory.path["artifact:/".Length..] + "/", StringComparison.Ordinal))).ToImmutableArray();
        ImmutableArray<NativeManifestCompilationUnit> sources = configuration.nativeBuild.sources.Select(path =>
        {
            string absolute = Path.GetFullPath(path, baseDirectory);
            SourceLanguage language = Path.GetExtension(path).Equals(".c", StringComparison.Ordinal) ? SourceLanguage.C : SourceLanguage.Cpp;
            return new NativeManifestCompilationUnit(generatedInputs?.RelocatePath(absolute) ?? absolute, language, language == SourceLanguage.C ? configuration.source.language == SourceLanguage.C ? configuration.source.languageStandard! : "c17"
                : configuration.source.language == SourceLanguage.Cpp ? configuration.source.languageStandard! : "c++23");
        }).Concat(generated.Where(artifact => artifact.relativePath.Contains("/src/", StringComparison.Ordinal)
            && Path.GetExtension(artifact.relativePath) is ".c" or ".cpp" or ".cxx" or ".cc").Select(artifact =>
                new NativeManifestCompilationUnit("artifact:/" + artifact.relativePath, Path.GetExtension(artifact.relativePath) == ".c" ? SourceLanguage.C : SourceLanguage.Cpp,
                    Path.GetExtension(artifact.relativePath) == ".c" ? configuration.source.language == SourceLanguage.C ? configuration.source.languageStandard! : "c17" : configuration.source.language == SourceLanguage.Cpp ? configuration.source.languageStandard! : "c++23")))
            .DistinctBy(source => source.path, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToImmutableArray();
        ImmutableArray<string> libraries = configuration.nativeBuild.libraries.Select(library => library.Contains('/') || library.Contains('\\') || Path.HasExtension(library) ? Path.GetFullPath(library, baseDirectory) : library).ToImmutableArray();
        ImmutableArray<InputDependency> external = originalInputs.files.Concat(configuration.source.headers.Concat(configuration.cpp.nativeShims.SelectMany(shim => shim.publicHeaders))
                .Select(path => Snapshot(Path.GetFullPath(path, baseDirectory))))
            .Concat(sources.Where(source => !source.path.StartsWith("artifact:/", StringComparison.Ordinal)).Select(source => Snapshot(source.path)))
            .Concat(libraries.Where(Path.IsPathFullyQualified).Select(Snapshot)).Select(input => generatedInputs?.Relocate(input) ?? input)
            .DistinctBy(input => input.path, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToImmutableArray();
        return new(configuration.library.id, configuration.library.nativeName, target, sources, generated, external, [])
        {
            artifactRoot = "../../..",
            providerId = configuration.nativeBuild.provider,
            preprocessingSources = generated.Where(artifact => artifact.relativePath.StartsWith(prefix + "src/", StringComparison.Ordinal) && artifact.mediaType is "text/x-c-header" or "text/x-c++-header")
                .Select(artifact => new NativeManifestCompilationUnit("artifact:/" + artifact.relativePath, configuration.source.language, configuration.source.languageStandard!)).ToImmutableArray(),
            outputKind = configuration.nativeBuild.outputKind ?? PlatformCapabilityCatalog.Get(target).defaultOutputKind,
            includeDirectories = configuration.source.includeDirectories.Select(path => Path.GetFullPath(path, baseDirectory)).Select(path => generatedInputs?.RelocatePath(path) ?? path)
                .Append("artifact:/" + prefix + "include").Distinct(StringComparer.Ordinal).ToImmutableArray(),
            systemIncludeDirectories = configuration.source.systemIncludeDirectories.Select(path => Path.GetFullPath(path, baseDirectory))
                .Concat(target.toolchain.systemIncludeFolders).Concat(target.toolchain.cxxSystemIncludeFolders).Distinct(StringComparer.Ordinal).ToImmutableArray(),
            defines = configuration.source.defines.AddRange(targetDefines),
            compilerArguments = configuration.nativeBuild.compilerArguments,
            linkerArguments = configuration.nativeBuild.linkerArguments,
            libraries = libraries,
            libraryDirectories = configuration.nativeBuild.libraryDirectories.Select(path => Path.GetFullPath(path, baseDirectory)).ToImmutableArray(),
            buildToolPath = configuration.nativeBuild.buildToolPath,
            auxiliaryToolPath = configuration.nativeBuild.auxiliaryToolPath,
            generationConfiguration = preparedFrontend?.workerPluginContext?.configuration ?? JsonSerializer.SerializeToElement(configuration).Clone(),
            generationPathBase = Path.GetFullPath(baseDirectory),
            generatedDirectories = generatedInputs?.contracts ?? [],
            workerPlugins = preparedFrontend?.workerPlugins ?? [],
            workerPluginContext = preparedFrontend?.workerPluginContext,
            sourceRetainFunctionBodies = preparedFrontend?.retainFunctionBodies,
            plugins = definition?.Plugins.Select(plugin =>
            {
                var settings = plugin.Configuration ?? throw new InvalidOperationException("A native manifest plugin omitted its frozen settings.");
                return new NativeManifestPluginActivation(plugin.Manifest?.path, plugin.Descriptor,
                    new(settings.id, settings.settings, settings.defaultPathBase, settings.valuePathBases), plugin.Manifest?.inputs ?? [],
                    plugin.Manifest?.missingInputs.Select(path => new MissingInputDependency(path, PhysicalPathResolver.Resolve(path))).ToImmutableArray() ?? []);
            }).ToImmutableArray() ?? []
        };
    }
    internal static SourceSnapshot CandidateExternalInputs(
        NativeBuildManifest manifest,
        string artifactRoot
    )
        => new(manifest.externalInputs.Select(input => input.path.StartsWith("artifact:/", StringComparison.Ordinal)
            ? input with { path = NativeBuildManifestLoader.Resolve(input.path, artifactRoot), resolvedPath = PhysicalPathResolver.Resolve(NativeBuildManifestLoader.Resolve(input.resolvedPath, artifactRoot)) }
            : input).ToImmutableArray(), []);
    private static InputDependency Snapshot(string path)
    {
        using FileStream source = File.OpenRead(path);
        return new(Path.GetFullPath(path), PhysicalPathResolver.Resolve(path), source.Length, new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(source))));
    }
}
