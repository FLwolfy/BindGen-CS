using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Composition;
using BGCS.Configuration;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;
using BGCS.Infrastructure.IO;

namespace BGCS;

/// <summary>Owns one complete immutable generated-input store until joint generation and publication finish.</summary>
internal sealed class SourceContributionCapture : IDisposable
{
    private static readonly UTF8Encoding s_utf8 = new(false, true);
    private static readonly StringComparer s_paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private readonly ImmutableArray<FileEntry> m_files;
    private readonly FileStream m_storeLease;
    private readonly string m_temporaryRoot;
    private readonly string m_physicalTemporaryRoot;
    private bool m_disposed;
    internal string storeRoot { get; }
    internal ImmutableArray<string> headers { get; }
    internal ImmutableArray<string> exposedHeaders { get; }
    internal ImmutableArray<string> includeDirectories { get; }
    internal ImmutableDictionary<string, string> sourcePathMappings { get; }
    internal SourceSnapshot snapshot { get; }

    private SourceContributionCapture(
        string storeRoot,
        ImmutableArray<FileEntry> files,
        FileStream storeLease,
        string temporaryRoot,
        string physicalTemporaryRoot
    ) {
        this.storeRoot = storeRoot;
        m_files = files;
        m_storeLease = storeLease;
        m_temporaryRoot = temporaryRoot;
        m_physicalTemporaryRoot = physicalTemporaryRoot;
        headers = files.Where(file => file.Artifact.role == SourceContributionRole.PublicHeader && file.Artifact.exposeToBindings)
            .Select(file => Physical(file.Relative)).ToImmutableArray();
        exposedHeaders = files.Where(file => file.Artifact.role == SourceContributionRole.PublicHeader && file.Artifact.exposeToBindings)
            .Select(file => file.Logical).ToImmutableArray();
        includeDirectories = files.Any(file => file.Artifact.role == SourceContributionRole.PublicHeader)
            ? [Path.Combine(storeRoot, "native", "include")] : [];
        sourcePathMappings = files.Where(file => file.Artifact.role is SourceContributionRole.PublicHeader or SourceContributionRole.NativeSource)
            .ToImmutableDictionary(file => Physical(file.Relative), file => file.Logical, s_paths);
        snapshot = new(files.Select(file => new InputDependency(Physical(file.Relative), PhysicalPathResolver.Resolve(Physical(file.Relative)),
            file.Artifact.content.Length, file.Digest)).ToImmutableArray(), []);
    }

    internal static async ValueTask<SourceContributionCapture?> CaptureAsync(
        FrozenExtensionRegistry registry,
        BindingConfiguration configuration,
        FrontendRequest source,
        GenerationInputTracker inputs,
        bool includeManaged,
        CancellationToken cancellationToken
    ) {
        if (registry.SourceContributors.IsEmpty) return null;
        ImmutableArray<FileEntry>.Builder files = ImmutableArray.CreateBuilder<FileEntry>();
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        List<ISourceContributor> services = [];
        SourceContributionCapture? captured = null;
        try
        {
            foreach (SourceContributorRegistration registration in registry.SourceContributors)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateServiceId(registration.Id);
                ISourceContributor service = registration.Factory() ?? throw new InvalidOperationException("A source contributor factory returned no service.");
                services.Add(service);
                if (service.id != registration.Id || service.priority != registration.Priority)
                    throw new InvalidOperationException("A source contributor changed its registered identity or priority.");
                inputs.Capture(service, source.target, cancellationToken);
                string runtimeNamespace = configuration.runtime.mode == "source" ? configuration.runtime.@namespace ?? configuration.api.@namespace + ".Runtime" : "BGCS.Runtime";
                SourceContributionRequest request = new(configuration.library.id, configuration.library.nativeName,
                    source with { snapshotExcludedDirectories = [] }, configuration.api.@namespace, configuration.api.name, runtimeNamespace)
                { apiStyle = configuration.api.style };
                SourceContribution contribution = await service.ContributeAsync(request, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("A source contributor returned no complete contribution.");
                if (service.id != registration.Id || service.priority != registration.Priority)
                    throw new InvalidOperationException("A source contributor changed its frozen identity during generation.");
                if (contribution.artifacts.IsDefault || contribution.evidence is not { level: EvidenceLevel.Declared or EvidenceLevel.UserAsserted } evidence
                    || string.IsNullOrWhiteSpace(evidence.origin))
                    throw new InvalidOperationException("Generated sources require an initialized file collection and explicit Declared or UserAsserted provenance.");
                foreach (SourceContributionArtifact artifact in contribution.artifacts)
                {
                    ArgumentNullException.ThrowIfNull(artifact);
                    SourceContributionArtifact frozen = Freeze(artifact, source.language);
                    if (!includeManaged && frozen.role == SourceContributionRole.ManagedSource) continue;
                    string roleRoot = RoleRoot(frozen.role);
                    string owned = (frozen.role == SourceContributionRole.ManagedSource ? registration.Id : "contributions/" + registration.Id) + "/" + frozen.relativePath;
                    string relative = roleRoot + "/" + owned;
                    if (!paths.Add(relative)) throw new InvalidOperationException("Two source contributions claim the same portable artifact path: " + relative);
                    string logical = "contributions/" + registration.Id + "/" + frozen.relativePath;
                    files.Add(new(relative, logical, frozen, new(Convert.ToHexStringLower(SHA256.HashData(frozen.content.AsSpan()))), registration.Id, registration.Priority, evidence));
                }
            }
            FileEntry[] ordered = files.OrderBy(file => file.Relative, StringComparer.Ordinal).ToArray();
            foreach (FileEntry file in ordered)
                for (int slash = file.Relative.IndexOf('/'); slash >= 0; slash = file.Relative.IndexOf('/', slash + 1))
                    if (paths.Contains(file.Relative[..slash])) throw new InvalidOperationException("Generated artifact file and directory paths overlap.");
            if (ordered.Length == 0) return null;
            string digest = StoreDigest(configuration.library.id, source.target.targetId.value, ordered);
            string temporaryRoot = Path.GetFullPath(Path.GetTempPath());
            string physicalTemporaryRoot = PhysicalPathResolver.Resolve(temporaryRoot);
            string parent = Path.Combine(physicalTemporaryRoot, "BGCS", "source-inputs", "v1");
            RejectLinks(parent);
            Directory.CreateDirectory(parent);
            string root = Path.Combine(parent, digest);
            FileStream lease = await AcquireAsync(Path.Combine(parent, digest + ".lock"), cancellationToken).ConfigureAwait(false);
            try
            {
                VerifyTemporaryRoot(temporaryRoot, physicalTemporaryRoot);
                RejectLinks(root);
                if (!Directory.Exists(root))
                {
                    List<string> createdFiles = [];
                    HashSet<string> createdDirectories = new(s_paths);
                    try
                    {
                        Directory.CreateDirectory(root);
                        createdDirectories.Add(root);
                        foreach (FileEntry file in ordered)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            string path = Path.GetFullPath(file.Relative, root);
                            string parentDirectory = Path.GetDirectoryName(path)!;
                            for (string? directory = parentDirectory; directory is not null && !s_paths.Equals(directory, root); directory = Path.GetDirectoryName(directory))
                                createdDirectories.Add(directory);
                            Directory.CreateDirectory(parentDirectory);
                            using FileStream output = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                            createdFiles.Add(path);
                            output.Write(file.Artifact.content.AsSpan());
                        }
                    }
                    catch (Exception failure)
                    {
                        try
                        {
                            // Delete only files created by this attempt, under the store
                            // lease. Never recursively clear an unexpected directory.
                            foreach (string path in createdFiles)
                            {
                                RejectLinks(path);
                                File.Delete(path);
                            }
                            foreach (string directory in createdDirectories.OrderByDescending(path => path.Length))
                            {
                                RejectLinks(directory);
                                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: false);
                            }
                        }
                        catch (Exception cleanup)
                        {
                            throw new AggregateException("Generated source capture and owned temporary-store cleanup failed.", failure, cleanup);
                        }
                        throw;
                    }
                }
                VerifyStore(root, ordered);
                VerifyTemporaryRoot(temporaryRoot, physicalTemporaryRoot);
                captured = new(root, ordered.ToImmutableArray(), lease, temporaryRoot, physicalTemporaryRoot);
                inputs.CaptureArgumentInputs(captured.snapshot);
                return captured;
            }
            catch { lease.Dispose(); throw; }
        }
        finally
        {
            List<Exception> failures = [];
            for (int index = services.Count - 1; index >= 0; index--)
                try
                {
                    if (services[index] is IAsyncDisposable asynchronous) await asynchronous.DisposeAsync().ConfigureAwait(false);
                    else if (services[index] is IDisposable disposable) disposable.Dispose();
                }
                catch (Exception failure) { failures.Add(failure); }
            if (failures.Count != 0)
            {
                try { captured?.Dispose(); }
                catch (Exception failure) { failures.Add(failure); }
                throw new AggregateException("Source contributor cleanup failed.", failures);
            }
        }
    }

    internal FrontendRequest Prepare(FrontendRequest request) => request with
    {
        headers = request.headers.Concat(headers).Distinct(s_paths).ToImmutableArray(),
        includeDirectories = request.includeDirectories.Concat(includeDirectories).Distinct(s_paths).ToImmutableArray(),
        sourcePathMappings = request.sourcePathMappings.SetItems(sourcePathMappings)
    };

    internal ImmutableArray<ArtifactDescriptor> PublishNative(
        IArtifactSink sink,
        string target
    )
        => Publish(sink, target, managed: false);

    internal ImmutableArray<ArtifactDescriptor> PublishManaged(
        IArtifactSink sink,
        string target
    )
        => Publish(sink, target, managed: true).Select(artifact => artifact with { relativePath = artifact.relativePath[(target.Length + "/managed/".Length)..] }).ToImmutableArray();

    private ImmutableArray<ArtifactDescriptor> Publish(
        IArtifactSink sink,
        string target,
        bool managed
    ) {
        Verify();
        ImmutableArray<ArtifactDescriptor>.Builder result = ImmutableArray.CreateBuilder<ArtifactDescriptor>();
        foreach (FileEntry file in m_files.Where(file => (file.Artifact.role == SourceContributionRole.ManagedSource) == managed))
        {
            string path = target + "/" + file.Relative;
            sink.WriteBytes(path, file.Artifact.content.ToArray());
            result.Add(new(path, file.Artifact.mediaType, file.Digest));
        }
        return result.ToImmutable();
    }

    internal ImmutableArray<NativeGeneratedInputMap> Mappings(string target)
    {
        ImmutableArray<NativeGeneratedDirectoryMapping> directories = m_files.Where(file => file.Artifact.role != SourceContributionRole.ManagedSource)
            .Select(file => RoleRoot(file.Artifact.role)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(role => new NativeGeneratedDirectoryMapping("source-contribution:" + Path.GetFileName(storeRoot) + ":" + role,
                Path.Combine(storeRoot, role.Replace('/', Path.DirectorySeparatorChar)), target + "/" + role,
                role is "native/include" or "native/src" ? string.Empty : null)).ToImmutableArray();
        return directories.IsEmpty ? [] : [new(Path.Combine(storeRoot, "native"), directories)];
    }

    internal SourceSnapshot NativeInputs(SourceSnapshot source)
    {
        HashSet<string> managed = m_files.Where(file => file.Artifact.role == SourceContributionRole.ManagedSource)
            .Select(file => Physical(file.Relative)).ToHashSet(s_paths);
        return source with { files = source.files.Where(file => !managed.Contains(file.path)).ToImmutableArray() };
    }

    internal void Verify()
    {
        VerifyTemporaryRoot(m_temporaryRoot, m_physicalTemporaryRoot);
        VerifyStore(storeRoot, m_files);
        VerifyTemporaryRoot(m_temporaryRoot, m_physicalTemporaryRoot);
    }

    private static void VerifyTemporaryRoot(
        string logical,
        string physical
    ) {
        if (!s_paths.Equals(PhysicalPathResolver.Resolve(logical), physical))
            throw new IOException("The generated input temporary root changed its physical identity.");
    }
    private string Physical(string relative) => Path.GetFullPath(relative, storeRoot);

    internal static void ValidateServiceId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        PluginDescriptorValidator.ValidateId(id);
        if (id.Length > 128)
            throw new ArgumentException("A source contributor identity cannot exceed 128 characters.", nameof(id));
        ValidatePath(id);
    }

    private static SourceContributionArtifact Freeze(
        SourceContributionArtifact artifact,
        SourceLanguage sourceLanguage
    ) {
        ValidatePath(artifact.relativePath);
        if (!Enum.IsDefined(artifact.role) || !Enum.IsDefined(artifact.language) || artifact.content.IsDefault || string.IsNullOrWhiteSpace(artifact.mediaType)
            || artifact.exposeToBindings && artifact.role != SourceContributionRole.PublicHeader)
            throw new InvalidOperationException("A generated file has an unknown role, missing content/media or invalid binding exposure.");
        string extension = Path.GetExtension(artifact.relativePath).ToLowerInvariant();
        bool native = artifact.role is SourceContributionRole.PublicHeader or SourceContributionRole.NativeSource;
        if (native && artifact.language is not (SourceLanguage.C or SourceLanguage.Cpp) || !native && artifact.language != SourceLanguage.None
            || artifact.role == SourceContributionRole.PublicHeader && (extension is not (".h" or ".hpp" or ".hxx" or ".hh")
                || sourceLanguage == SourceLanguage.C && artifact.language == SourceLanguage.Cpp)
            || artifact.role == SourceContributionRole.NativeSource && (artifact.language == SourceLanguage.C ? extension != ".c" : extension is not (".cpp" or ".cxx" or ".cc"))
            || artifact.role == SourceContributionRole.ManagedSource && extension != ".cs")
            throw new InvalidOperationException("A generated source role, suffix and language must agree with its original parse session.");
        string mediaType = artifact.role switch
        {
            SourceContributionRole.PublicHeader => artifact.language == SourceLanguage.C ? "text/x-c-header" : "text/x-c++-header",
            SourceContributionRole.NativeSource => artifact.language == SourceLanguage.C ? "text/x-c" : "text/x-c++",
            SourceContributionRole.ManagedSource => "text/x-csharp",
            _ => artifact.mediaType
        };
        if (artifact.role != SourceContributionRole.Resource)
        {
            string[] accepted = artifact.role switch
            {
                SourceContributionRole.PublicHeader when artifact.language == SourceLanguage.C => ["text/x-c-header", "text/x-chdr"],
                SourceContributionRole.PublicHeader => ["text/x-c++-header", "text/x-c++hdr"],
                SourceContributionRole.NativeSource when artifact.language == SourceLanguage.C => ["text/x-c", "text/x-csrc"],
                SourceContributionRole.NativeSource => ["text/x-c++", "text/x-c++src"],
                _ => ["text/x-csharp"]
            };
            if (!accepted.Contains(artifact.mediaType, StringComparer.Ordinal)) throw new InvalidOperationException("A generated source media type does not match its explicit role and language.");
            _ = s_utf8.GetString(artifact.content.AsSpan());
        }
        return artifact with { mediaType = mediaType, content = artifact.content.ToArray().ToImmutableArray() };
    }

    private static string RoleRoot(SourceContributionRole role) => role switch
    {
        SourceContributionRole.PublicHeader => "native/include",
        SourceContributionRole.NativeSource => "native/src",
        SourceContributionRole.ManagedSource => "managed/Extensions",
        SourceContributionRole.Resource => "native/resources",
        _ => throw new InvalidOperationException("An unknown generated source role cannot own a directory.")
    };

    private static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || Path.IsPathRooted(path)) throw new InvalidOperationException("Generated file paths must be portable relative paths.");
        foreach (string segment in path.Split('/'))
        {
            string stem = segment.Split('.')[0];
            if (segment is "" or "." or ".." || segment.EndsWith(' ') || segment.EndsWith('.')
                || segment.Any(character => character < 32 || "<>:\"|?*".Contains(character)) || stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
                || stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && stem[3] is >= '1' and <= '9')
                throw new InvalidOperationException("A generated file path has a reserved, escaping or nonportable component.");
        }
    }

    private static string StoreDigest(
        string library,
        string target,
        IEnumerable<FileEntry> files
    ) {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Text(library); Text(target);
        foreach (FileEntry file in files)
        {
            Text(file.Service); Text(file.Priority.ToString(System.Globalization.CultureInfo.InvariantCulture)); Text(file.Relative);
            Text(file.Artifact.mediaType); Text(file.Artifact.role.ToString()); Text(file.Artifact.language.ToString()); Text(file.Artifact.exposeToBindings.ToString());
            Text(file.Evidence.level.ToString()); Text(file.Evidence.origin); Text(file.Digest.value);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
        void Text(string value)
        {
            byte[] content = s_utf8.GetBytes(value);
            Span<byte> length = stackalloc byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(length, content.Length);
            hash.AppendData(length); hash.AppendData(content);
        }
    }

    private static void VerifyStore(
        string root,
        IEnumerable<FileEntry> files
    ) {
        RejectLinks(root);
        Dictionary<string, FileEntry> expected = files.ToDictionary(file => Path.GetFullPath(file.Relative, root), s_paths);
        string[] actual = NativeGeneratedInputRelocation.EnumerateFiles(root).ToArray();
        if (actual.Length != expected.Count || actual.Any(path => !expected.ContainsKey(path))) throw new IOException("A generated input store contains an undeclared or missing file.");
        foreach (string path in actual)
        {
            using FileStream input = File.OpenRead(path);
            FileEntry expectedFile = expected[path];
            if (input.Length != expectedFile.Artifact.content.Length || Convert.ToHexStringLower(SHA256.HashData(input)) != expectedFile.Digest.value)
                throw new IOException("A generated input store changed its immutable bytes.");
        }
    }

    private static void RejectLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Generated input stores cannot traverse a symbolic link or reparse point.");
    }

    private static async ValueTask<FileStream> AcquireAsync(
        string path,
        CancellationToken cancellationToken
    ) {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectLinks(path);
            try { return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose); }
            catch (IOException) { await Task.Delay(25, cancellationToken).ConfigureAwait(false); }
        }
    }

    public void Dispose()
    {
        if (m_disposed) return;
        m_disposed = true;
        try
        {
            Verify();
            HashSet<string> directories = new(s_paths) { storeRoot };
            foreach (FileEntry file in m_files)
            {
                string path = Physical(file.Relative);
                File.Delete(path);
                for (string? parent = Path.GetDirectoryName(path); parent is not null && !s_paths.Equals(parent, storeRoot); parent = Path.GetDirectoryName(parent))
                    directories.Add(parent);
            }
            foreach (string directory in directories.OrderByDescending(path => path.Length)) Directory.Delete(directory, recursive: false);
        }
        finally { m_storeLease.Dispose(); }
    }

    private sealed record FileEntry(
        string Relative,
        string Logical,
        SourceContributionArtifact Artifact,
        ContentDigest Digest,
        string Service,
        int Priority,
        Evidence Evidence
    );
}
