using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Requests;
using BGCS.Infrastructure.IO;
using BGCS.Infrastructure.Workers;
using BGCS.Contracts.Common;
using BGCS.Composition;
using BGCS.Contracts.Targets;
using BGCS.Platforms;
using BGCS.Platforms.Assets;
using BGCS.Contracts.Plugins;
using BGCS.Infrastructure.Plugins;

namespace BGCS;

internal sealed record LoadedNativeBuildManifest(
    NativeBuildManifest manifest,
    string artifactRoot,
    SourceSnapshot snapshot
);

internal static class NativeBuildManifestLoader
{
    internal static LoadedNativeBuildManifest Load(
        string path,
        CancellationToken cancellationToken
    ) {
        string absolute = Path.GetFullPath(path);
        using FileStream file = File.OpenRead(absolute);
        if (file.Length is <= 0 or > 16 * 1024 * 1024) throw new InvalidDataException("The neutral native build manifest is empty or exceeds 16 MiB.");
        byte[] bytes = new byte[(int)file.Length];
        file.ReadExactly(bytes);
        using (JsonDocument document = JsonDocument.Parse(bytes))
        {
            if (!document.RootElement.TryGetProperty("schemaVersion", out _) || !document.RootElement.TryGetProperty("contractsMajor", out _))
                throw new InvalidDataException("A native build manifest requires explicit schemaVersion and contractsMajor fields.");
            CheckDuplicates(document.RootElement);
        }
        NativeBuildManifest manifest = JsonSerializer.Deserialize<NativeBuildManifest>(bytes, WorkerProtocol.jsonOptions) ?? throw new InvalidDataException("The native build manifest is null.");
        if (manifest.schemaVersion != 1 || manifest.contractsMajor != BGCS.Contracts.Versioning.ContractVersion.C_MAJOR) throw new InvalidDataException("The native build manifest schema or Contracts major version is unsupported.");
        if (manifest.target is null || manifest.sources.IsDefaultOrEmpty || manifest.preprocessingSources.IsDefault || manifest.artifacts.IsDefault || manifest.externalInputs.IsDefault || manifest.requiredExports.IsDefault || manifest.plugins.IsDefault || manifest.workerPlugins.IsDefault || manifest.extensionInputs.IsDefault || manifest.factVerificationSources.IsDefault || manifest.generatedDirectories.IsDefault)
            throw new InvalidDataException("The native build manifest has incomplete target, compilation or content-lock fields.");
        if (manifest.sourceFacts is null)
        {
            if (!manifest.factVerificationSources.IsEmpty) throw new InvalidDataException("Native effective-source verification inputs require their frozen selected facts.");
            if (manifest.sourceRetainFunctionBodies is not null) throw new InvalidDataException("Native source body-retention options require their frozen selected facts.");
        }
        else
        {
            if (manifest.sourceRetainFunctionBodies is null)
                throw new InvalidDataException("Native source facts require their explicit sourceRetainFunctionBodies extraction option; older incomplete manifests must be regenerated.");
            if (manifest.factVerificationSources.IsEmpty || manifest.sourceFacts.targetId != manifest.target.targetId.value || manifest.sourceFacts.triple != manifest.target.triple
                || manifest.sourceFacts.byteOrder != manifest.target.byteOrder || manifest.sourceFacts.language == SourceLanguage.None)
                throw new InvalidDataException("Native generated source facts require exact target identity, language and paired verification inputs.");
            foreach (string verification in manifest.factVerificationSources)
                if (!verification.StartsWith("artifact:/", StringComparison.Ordinal)
                    || !manifest.preprocessingSources.Any(source => source.path == verification && source.language == manifest.sourceFacts.language)
                    || !manifest.artifacts.Any(artifact => "artifact:/" + artifact.relativePath == verification))
                    throw new InvalidDataException("A native effective-source verification unit must be a content-locked generated preprocessing-only input.");
        }
        if (string.IsNullOrWhiteSpace(manifest.libraryName) || manifest.libraryName is "." or ".." || manifest.libraryName.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-' and not '.'))
            throw new InvalidDataException("The native library name is not a portable filename stem.");
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.libraryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.providerId);
        if (!Enum.IsDefined(manifest.outputKind) || manifest.includeDirectories.IsDefault || manifest.systemIncludeDirectories.IsDefault || manifest.defines.IsDefault
            || manifest.compilerArguments.IsDefault || manifest.linkerArguments.IsDefault || manifest.libraries.IsDefault || manifest.libraryDirectories.IsDefault)
            throw new InvalidDataException("A native build manifest has uninitialized or unsupported construction options.");
        if (manifest.managedRuntime is null)
        {
            if (manifest.importModuleName is not null || manifest.nativeArtifactFilename is not null)
                throw new InvalidDataException("A native manifest requires its generated managed runtime, import identity and output filename together.");
        }
        else
        {
            ManagedRuntimeProfile declared = manifest.managedRuntime;
            if (string.IsNullOrWhiteSpace(manifest.importModuleName) || !IsPortableFilename(manifest.nativeArtifactFilename))
                throw new InvalidDataException("A generated-binding native manifest requires an exact import identity and portable output filename.");
            ManagedRuntimeProfile resolved = ManagedRuntimeResolver.Resolve(manifest.target, declared.kind, manifest.outputKind);
            if (declared.id != resolved.id || declared.moduleMechanism != resolved.moduleMechanism || declared.requiresStaticCallbackThunks != resolved.requiresStaticCallbackThunks
                || declared.allowsBlockingDisposal != resolved.allowsBlockingDisposal || declared.libraryImportConvention != resolved.libraryImportConvention || declared.aggregateAbi != resolved.aggregateAbi
                || declared.importModes.IsDefault || !declared.importModes.SequenceEqual(resolved.importModes))
                throw new InvalidDataException("A native manifest changed its resolved managed runtime consumption contract.");
            string identity = NativeOutputLayout.ResolveImportModuleName(manifest.target, resolved, manifest.importModuleName, manifest.nativeArtifactFilename);
            if (!string.Equals(identity, manifest.importModuleName, StringComparison.Ordinal))
                throw new InvalidDataException("A native manifest's declared filename changes its generated managed import identity.");
        }
        if (manifest.requiredExports.Any(string.IsNullOrWhiteSpace) || manifest.requiredExports.Distinct(StringComparer.Ordinal).Count() != manifest.requiredExports.Length)
            throw new InvalidDataException("A native build manifest has empty or duplicated required exports.");
        if (manifest.artifacts.Select(artifact => artifact.relativePath).Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).Count() != manifest.artifacts.Length)
            throw new InvalidDataException("A native build manifest has colliding generated input locks.");
        if (Path.IsPathRooted(manifest.artifactRoot)) throw new InvalidDataException("A native artifact root must be relative to its declaring manifest.");
        string root = Path.GetFullPath(manifest.artifactRoot, Path.GetDirectoryName(absolute)!);
        ImmutableArray<DirectoryInputDependency> generatedDirectories = ValidateGeneratedDirectories(manifest, root, cancellationToken);
        // Publication replaces the private candidate root with artifact:/. Only owned
        // membership exclusions may use that marker; actual input locks remain absolute.
        manifest = manifest with
        {
            externalInputs = manifest.externalInputs.Select(input => ResolveGeneratedFile(input, manifest, root)).ToImmutableArray(),
            extensionInputs = manifest.extensionInputs.Select(snapshot => snapshot is null || snapshot.excludedDirectories.IsDefault || snapshot.files.IsDefault || snapshot.absentInputs.IsDefault
                ? snapshot! : snapshot with
                {
                    files = snapshot.files.Select(input => ResolveGeneratedFile(input, manifest, root)).ToImmutableArray(),
                    absentInputs = snapshot.absentInputs.Select(input =>
                    {
                        if (!input.path.StartsWith("artifact:/", StringComparison.Ordinal)) return input;
                        if (input.path != input.resolvedPath || !manifest.generatedDirectories.Any(directory => input.path.StartsWith(directory.path + "/", StringComparison.Ordinal)))
                            throw new InvalidDataException("A relocated generated absence lock requires a declared generated directory.");
                        string resolved = Resolve(input.path, root);
                        return input with { path = resolved, resolvedPath = PhysicalPathResolver.Resolve(resolved) };
                    }).ToImmutableArray(),
                    excludedDirectories = snapshot.excludedDirectories.Select(exclusion => Resolve(exclusion, root))
                        .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToImmutableArray()
                }).ToImmutableArray()
        };
        ImmutableArray<InputDependency>.Builder inputs = ImmutableArray.CreateBuilder<InputDependency>();
        ImmutableArray<MissingInputDependency>.Builder missing = ImmutableArray.CreateBuilder<MissingInputDependency>();
        inputs.Add(new(absolute, PhysicalPathResolver.Resolve(absolute), bytes.Length, new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(bytes)))));
        foreach (var artifact in manifest.artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string artifactPath = Resolve("artifact:/" + artifact.relativePath, root);
            using FileStream artifactFile = File.OpenRead(artifactPath);
            string hash = Convert.ToHexStringLower(SHA256.HashData(artifactFile));
            if (hash != artifact.digest.value) throw new IOException("A generated native manifest input changed after generation: " + artifact.relativePath);
            inputs.Add(new(artifactPath, PhysicalPathResolver.Resolve(artifactPath), artifactFile.Length, artifact.digest));
        }
        SourceSnapshot external = new(manifest.externalInputs, []);
        if (!SourceSnapshotVerifier.Matches(external, cancellationToken)) throw new IOException("A declared native source/header/library changed after manifest generation.");
        inputs.AddRange(manifest.externalInputs);
        foreach (SourceSnapshot snapshot in manifest.extensionInputs)
        {
            if (snapshot is null || snapshot.files.IsDefault || snapshot.searchDirectories.IsDefault || snapshot.absentInputs.IsDefault || snapshot.excludedDirectories.IsDefault
                || snapshot.files.Any(input => input is null || !Path.IsPathFullyQualified(input.path) || !Path.IsPathFullyQualified(input.resolvedPath))
                || snapshot.searchDirectories.Any(input => input is null || !Path.IsPathFullyQualified(input.path) || !Path.IsPathFullyQualified(input.resolvedPath))
                || snapshot.absentInputs.Any(input => input is null || !Path.IsPathFullyQualified(input.path) || !Path.IsPathFullyQualified(input.resolvedPath))
                || snapshot.excludedDirectories.Any(path => !Path.IsPathFullyQualified(path)))
                throw new InvalidDataException("Native generation extension inputs require complete absolute file, directory and absence locks.");
            if (SourceSnapshotVerifier.GetMismatch(snapshot, cancellationToken) is string mismatch)
                throw new IOException("A native generation extension input changed after manifest generation. " + mismatch);
            inputs.AddRange(snapshot.files);
            missing.AddRange(snapshot.absentInputs);
        }
        foreach (NativeManifestPluginActivation plugin in manifest.plugins)
        {
            PluginDescriptorValidator.Freeze(plugin.descriptor);
            if (plugin.configuration.id != plugin.descriptor.id || plugin.inputs.IsDefault || plugin.missingInputs.IsDefault)
                throw new InvalidDataException("A native manifest plugin has incomplete compatibility, settings or input locks.");
            if (plugin.manifestPath is string pluginPath)
            {
                if (!Path.IsPathFullyQualified(pluginPath) || plugin.inputs.IsEmpty || !plugin.inputs.Any(input => string.Equals(input.path, pluginPath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
                    throw new InvalidDataException("A portable native manifest plugin requires an absolute content-locked entry manifest.");
            }
            else if (!plugin.inputs.IsEmpty || !plugin.missingInputs.IsEmpty) throw new InvalidDataException("A factory-only native plugin requirement cannot claim portable executable inputs.");
            if (!SourceSnapshotVerifier.Matches(new(plugin.inputs, []) { absentInputs = plugin.missingInputs }, cancellationToken))
                throw new IOException("A native manifest plugin implementation or dependency-metadata identity changed after generation.");
            inputs.AddRange(plugin.inputs); missing.AddRange(plugin.missingInputs);
        }
        if (manifest.plugins.Select(plugin => plugin.descriptor.id).Distinct(StringComparer.Ordinal).Count() != manifest.plugins.Length)
            throw new InvalidDataException("A native manifest contains duplicate plugin activation identities.");
        foreach (LockedPluginManifest worker in ValidateWorkerPlugins(manifest))
        {
            inputs.AddRange(worker.inputs);
            missing.AddRange(worker.missingInputs.Select(path => new MissingInputDependency(path, PhysicalPathResolver.Resolve(path))));
        }
        if (manifest.generationConfiguration is not null && (manifest.generationPathBase is null || !Path.IsPathFullyQualified(manifest.generationPathBase)))
            throw new InvalidDataException("Frozen generation settings require their original absolute path base.");
        if (manifest.interop is not null && (manifest.interop.libraryId != manifest.libraryId || manifest.interop.targetId != manifest.target.targetId.value))
            throw new InvalidDataException("A native manifest's validation model changed its library or opaque target identity.");
        if (manifest.sources.Concat(manifest.preprocessingSources).Select(source => source.path).Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).Count() != manifest.sources.Length + manifest.preprocessingSources.Length)
            throw new InvalidDataException("Native compiled and preprocessing-only source inputs must have distinct identities.");
        foreach (NativeManifestCompilationUnit source in manifest.preprocessingSources)
            if (!manifest.sources.Any(compiled => compiled.language == source.language && compiled.standard == source.standard))
                throw new InvalidDataException("A native preprocessing-only source requires a compiled source with matching language and standard.");
        foreach (NativeManifestCompilationUnit source in manifest.sources.Concat(manifest.preprocessingSources))
        {
            if (source.language is not (SourceLanguage.C or SourceLanguage.Cpp) || string.IsNullOrWhiteSpace(source.standard))
                throw new InvalidDataException("A native compilation unit requires an explicit C or C++ language and standard.");
            string resolved = Resolve(source.path, root);
            if (source.path.StartsWith("artifact:/", StringComparison.Ordinal) && !manifest.artifacts.Any(artifact => artifact.relativePath == source.path["artifact:/".Length..]))
                throw new InvalidDataException("A generated native source has no content lock in its manifest.");
            if (!source.path.StartsWith("artifact:/", StringComparison.Ordinal) && !manifest.externalInputs.Any(input => string.Equals(input.path, resolved, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
                throw new InvalidDataException("An external native source has no content lock in its manifest.");
        }
        return new(manifest, root, new(inputs.DistinctBy(input => input.path, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToImmutableArray(), generatedDirectories)
        { absentInputs = missing.DistinctBy(input => input.path, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToImmutableArray() });
    }

    private static ImmutableArray<LockedPluginManifest> ValidateWorkerPlugins(NativeBuildManifest manifest)
    {
        if (manifest.workerPlugins.IsEmpty)
        {
            if (manifest.workerPluginContext is not null || manifest.plugins.Any(plugin => plugin.descriptor.services.Any(service => service.port == ExtensionPort.FrontendFactContributor)))
                throw new InvalidDataException("A native fact contributor requires its complete worker activation and fingerprint context.");
            return [];
        }
        CacheFingerprintContext context = manifest.workerPluginContext
            ?? throw new InvalidDataException("Native worker activations require their complete frozen fingerprint context.");
        if (manifest.sourceFacts is null || manifest.generationConfiguration is not { } configuration || manifest.generationPathBase is null
            || !Path.IsPathFullyQualified(context.pathBase)
            || !string.Equals(Path.GetFullPath(context.pathBase), Path.GetFullPath(manifest.generationPathBase), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            || !JsonElement.DeepEquals(context.configuration, configuration)
            || context.target.targetId != manifest.target.targetId || context.target.triple != manifest.target.triple
            || context.target.platformId != manifest.target.platformId || context.target.architectureId != manifest.target.architectureId
            || context.target.abiId != manifest.target.abiId || context.target.byteOrder != manifest.target.byteOrder)
            throw new InvalidDataException("Native worker fingerprint settings, source facts or concrete target identity differ from generation.");
        ImmutableArray<LockedPluginManifest>.Builder locked = ImmutableArray.CreateBuilder<LockedPluginManifest>();
        var identities = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        foreach (WorkerPluginActivation activation in manifest.workerPlugins)
        {
            if (activation is null || activation.manifest is null || !activation.manifest.workerOnly
                || activation.manifest.workerParserIdentity != WorkerProtocol.C_PARSER_IDENTITY || activation.parserIdentity != WorkerProtocol.C_PARSER_IDENTITY
                || !Path.IsPathFullyQualified(activation.manifestPath) || !Path.IsPathFullyQualified(activation.pathBase)
                || activation.inputs.IsDefaultOrEmpty || activation.missingInputs.IsDefault || activation.valuePathBases is null
                || activation.settings.ValueKind != JsonValueKind.Object || !identities.Add(activation.manifest.plugin.id)
                || activation.manifest.plugin.services.IsDefaultOrEmpty
                || activation.manifest.plugin.services.Any(service => service.port != ExtensionPort.FrontendFactContributor))
                throw new InvalidDataException("A native worker activation has incomplete or incompatible parser, settings or executable locks.");
            NativeManifestPluginActivation companion = manifest.plugins.SingleOrDefault(plugin => plugin.descriptor.id == activation.manifest.plugin.id)
                ?? throw new InvalidDataException("A native worker activation omitted its portable plugin requirement.");
            if (!string.Equals(companion.manifestPath, activation.manifestPath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                || !Same(companion.descriptor, activation.manifest.plugin) || !Same(companion.configuration.settings, activation.settings)
                || !Same(companion.configuration.valuePathBases, activation.valuePathBases)
                || companion.configuration.defaultPathBase != activation.pathBase || !Same(companion.inputs, activation.inputs)
                || !companion.missingInputs.Select(input => input.path).SequenceEqual(activation.missingInputs, StringComparer.Ordinal))
                throw new InvalidDataException("A native worker activation differs from its exact frozen plugin settings or executable requirement.");
            LockedPluginManifest actual = new PluginManifestReader().Read(activation.manifestPath);
            if (!Same(actual.manifest, activation.manifest) || !Same(actual.inputs, activation.inputs)
                || !actual.missingInputs.SequenceEqual(activation.missingInputs, StringComparer.Ordinal))
                throw new IOException("A native worker contributor's executable closure changed after generation.");
            locked.Add(actual);
        }
        if (manifest.plugins.Any(plugin => plugin.descriptor.services.Any(service => service.port == ExtensionPort.FrontendFactContributor)
            && !identities.Contains(plugin.descriptor.id)))
            throw new InvalidDataException("A native manifest omitted a required worker-only contributor activation.");
        return locked.ToImmutable();

        static bool Same<T>(
            T left,
            T right
        ) => JsonElement.DeepEquals(JsonSerializer.SerializeToElement(left, WorkerProtocol.jsonOptions), JsonSerializer.SerializeToElement(right, WorkerProtocol.jsonOptions));
    }
    private static InputDependency ResolveGeneratedFile(
        InputDependency input,
        NativeBuildManifest manifest,
        string root
    ) {
        if (input is null) throw new InvalidDataException("A native input content lock cannot be null.");
        if (!input.path.StartsWith("artifact:/", StringComparison.Ordinal)) return input;
        NativeGeneratedFileContract? member = manifest.generatedDirectories.SelectMany(directory => directory.files
            .Where(file => input.path == directory.path + "/" + file.relativePath)).FirstOrDefault();
        if (input.path != input.resolvedPath || member is null || member.length != input.length || member.digest != input.digest)
            throw new InvalidDataException("A relocated generated file requires an equal portable directory and artifact content lock.");
        string resolved = Resolve(input.path, root);
        return input with { path = resolved, resolvedPath = PhysicalPathResolver.Resolve(resolved) };
    }
    private static ImmutableArray<DirectoryInputDependency> ValidateGeneratedDirectories(
        NativeBuildManifest manifest,
        string root,
        CancellationToken cancellationToken
    ) {
        StringComparer paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        HashSet<string> identities = new(StringComparer.Ordinal), destinations = new(paths), logicalIdentities = new(StringComparer.Ordinal);
        ImmutableArray<DirectoryInputDependency>.Builder snapshots = ImmutableArray.CreateBuilder<DirectoryInputDependency>();
        foreach (NativeGeneratedDirectoryContract directory in manifest.generatedDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (directory is null || string.IsNullOrWhiteSpace(directory.identity) || !identities.Add(directory.identity) || !directory.path.StartsWith("artifact:/", StringComparison.Ordinal)
                || directory.files.IsDefault || !destinations.Add(directory.path)) throw new InvalidDataException("Generated input directories require complete unique portable identities.");
            NativeGeneratedInputRelocation.Portable(directory.path["artifact:/".Length..]);
            string physical = Resolve(directory.path, root);
            if (!Directory.Exists(physical)) throw new IOException("A relocated generated input directory disappeared: " + directory.path);
            Dictionary<string, NativeGeneratedFileContract> expected = new(paths);
            foreach (NativeGeneratedFileContract file in directory.files)
            {
                if (file is null || file.length < 0) throw new InvalidDataException("A generated input member has no complete content identity.");
                NativeGeneratedInputRelocation.Portable(file.relativePath);
                if (file.logicalSourcePath is string logical)
                {
                    NativeGeneratedInputRelocation.Portable(logical);
                    if (!logicalIdentities.Add(logical)) throw new InvalidDataException("Two generated files cannot claim the same logical frontend source identity.");
                }
                if (!expected.TryAdd(file.relativePath, file) || !manifest.artifacts.Any(artifact => "artifact:/" + artifact.relativePath == directory.path + "/" + file.relativePath && artifact.digest == file.digest))
                    throw new InvalidDataException("Every generated directory member requires a unique equal artifact content lock.");
            }
            string[] artifactMembers = manifest.artifacts.Where(artifact => ("artifact:/" + artifact.relativePath).StartsWith(directory.path + "/", StringComparison.Ordinal))
                .Select(artifact => artifact.relativePath[(directory.path.Length - "artifact:/".Length + 1)..]).ToArray();
            if (artifactMembers.Length != expected.Count || artifactMembers.Any(relative => !expected.ContainsKey(relative)))
                throw new InvalidDataException("A generated directory must declare the complete published artifact membership of its role.");
            ImmutableArray<string> actual = NativeGeneratedInputRelocation.EnumerateFiles(physical);
            if (actual.Length != expected.Count) throw new IOException("Relocated generated input membership changed: " + directory.path);
            foreach (string path in actual)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relative = Path.GetRelativePath(physical, path).Replace('\\', '/');
                if (!expected.TryGetValue(relative, out NativeGeneratedFileContract? file) || (NativeGeneratedInputRelocation.Capture(path, relative) with { logicalSourcePath = file.logicalSourcePath }) != file)
                    throw new IOException("Relocated generated input content or membership changed: " + directory.path + "/" + relative);
            }
            snapshots.Add(DirectoryMembershipDigest.Capture(physical, cancellationToken: cancellationToken));
        }
        string[] roots = destinations.ToArray();
        for (int left = 0; left < roots.Length; left++)
            for (int right = left + 1; right < roots.Length; right++)
                if (roots[left].StartsWith(roots[right] + "/", comparison) || roots[right].StartsWith(roots[left] + "/", comparison))
                    throw new InvalidDataException("Relocated generated input directories cannot overlap.");
        return snapshots.ToImmutable();
    }
    private static bool IsPortableFilename(string? name) => !string.IsNullOrWhiteSpace(name) && name is not ("." or "..")
        && name.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.')
        && Path.GetFileName(name) == name;
    internal static bool Matches(
        LoadedNativeBuildManifest loaded,
        CancellationToken cancellationToken
    )
        => SourceSnapshotVerifier.Matches(loaded.snapshot, cancellationToken)
            && loaded.manifest.extensionInputs.All(snapshot => SourceSnapshotVerifier.Matches(snapshot, cancellationToken));
    internal static string Resolve(
        string value,
        string artifactRoot
    ) {
        const string marker = "artifact:/";
        if (!value.StartsWith(marker, StringComparison.Ordinal))
        {
            if (!Path.IsPathFullyQualified(value)) throw new InvalidDataException("Native external paths must be absolute; generated paths use artifact:/.");
            return Path.GetFullPath(value);
        }
        string relative = value[marker.Length..];
        if (relative.Length == 0) return artifactRoot;
        if (relative.Contains('\\') || relative.Contains(':') || relative.Split('/').Any(component => component is "" or "." or ".."))
            throw new InvalidDataException("An artifact:/ path must remain within the declared native artifact root.");
        string resolved = Path.GetFullPath(relative, artifactRoot);
        string physicalRoot = PhysicalPathResolver.Resolve(artifactRoot), physical = PhysicalPathResolver.Resolve(resolved);
        if (!physical.Equals(physicalRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            && !physical.StartsWith(Path.TrimEndingDirectorySeparator(physicalRoot) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("An artifact:/ native input follows a link outside its declared artifact root.");
        return resolved;
    }
    private static void CheckDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            System.Collections.Generic.HashSet<string> names = new(StringComparer.Ordinal);
            foreach (JsonProperty property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("The native build manifest contains duplicate JSON properties.");
                CheckDuplicates(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (JsonElement item in value.EnumerateArray()) CheckDuplicates(item);
    }
}
