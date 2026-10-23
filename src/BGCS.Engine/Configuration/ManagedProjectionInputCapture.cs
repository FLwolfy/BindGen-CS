using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BGCS.Configuration;
using BGCS.Core.Common;
using BGCS.Core.Hosting;
using BGCS.Core.Inputs;
using BGCS.Core.Requests;
using BGCS.Infrastructure.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace BGCS.Configuration;

/// <summary>Captures the actual managed static projection inputs before lookup, without loading or executing consumer code.</summary>
internal sealed class ManagedProjectionInputCapture
{
    private const int C_IMAGE_LIMIT = 64 * 1024 * 1024;
    private const int C_SOURCE_LIMIT = 16 * 1024 * 1024;
    private const long C_TOTAL_LIMIT = 512L * 1024 * 1024;
    private readonly IHostPathService m_paths;

    private ManagedProjectionInputCapture(
        ManagedProjectionInputs inputs,
        SourceSnapshot snapshot,
        IHostPathService paths
    ) { this.inputs = inputs; this.snapshot = snapshot; m_paths = paths; }

    internal ManagedProjectionInputs inputs { get; }
    internal SourceSnapshot snapshot { get; }

    internal void Verify(CancellationToken cancellationToken)
    {
        if (SourceSnapshotVerifier.GetMismatch(snapshot, m_paths, cancellationToken) is string mismatch)
            throw new IOException("Managed projection inputs changed during generation; the candidate cannot be reused or published. " + mismatch);
    }

    internal static ManagedProjectionInputCapture? Capture(
        ManagedProjectionInputConfiguration configuration,
        string baseDirectory,
        IHostPathService hostPaths,
        CancellationToken cancellationToken,
        bool includeFramework = false
    ) {
        var m_paths = hostPaths.comparer;
        if (configuration.references.IsDefault || configuration.sources.IsDefault || configuration.defines.IsDefault)
            throw new ArgumentException("Managed projection input collections must be initialized.");
        if (!includeFramework && configuration.references.IsEmpty && configuration.sources.IsEmpty && configuration.defines.IsEmpty) return null;
        if (configuration.references.Length > 1024 || configuration.sources.Length > 256 || configuration.defines.Length > 1024)
            throw new ArgumentException("Managed projection inputs exceed 1024 references, 256 sources or 1024 symbols.");
        foreach (string symbol in configuration.defines)
            if (string.IsNullOrWhiteSpace(symbol) || symbol.Length > 512 || !SyntaxFacts.IsValidIdentifier(symbol))
                throw new ArgumentException("A managed preprocessor symbol must be a bounded C# identifier.");

        Dictionary<string, CapturedAssembly> assemblies = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> paths = new(m_paths);
        Dictionary<string, InputDependency> files = new(m_paths);
        Dictionary<string, MissingInputDependency> absent = new(m_paths);
        var sources = ImmutableArray.CreateBuilder<ManagedSourceInput>();
        Queue<CapturedAssembly> pending = new();
        long total = 0;
        string[] explicitReferences = configuration.references.Select(path => FullPath(path, baseDirectory)).Distinct(m_paths).ToArray();
        string[] roots = explicitReferences.Select(path => Path.GetDirectoryName(path)!).Distinct(m_paths).ToArray();

        // Use the same framework metadata universe as the backend's default compiler.
        // These are metadata images, not a promise that full SDK compilation passed.
        string frameworkDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)
            ?? throw new NotSupportedException("Managed constant evaluation requires a physical .NET framework metadata closure.");
        string[] framework = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(path => m_paths.Equals(Path.GetDirectoryName(path), frameworkDirectory)).Distinct(m_paths).Order(StringComparer.Ordinal).ToArray();
        if (framework.Length == 0) throw new NotSupportedException("The executing .NET framework metadata closure is unavailable.");
        foreach (string path in framework) AddAssembly(path, isFramework: true);
        foreach (string path in explicitReferences) AddAssembly(path, isFramework: false);
        foreach (string declared in configuration.sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = FullPath(declared, baseDirectory);
            if (!paths.Add(path)) throw new ArgumentException("Managed source and assembly paths must be distinct: " + path);
            if (sources.Count >= 256) throw new ArgumentException("Managed projection inputs exceed 256 sources.");
            (ImmutableArray<byte> image, ContentDigest digest) = Read(path, C_SOURCE_LIMIT, allowEmpty: true);
            using MemoryStream stream = new(image.ToArray(), writable: false);
            SourceText text;
            try { text = SourceText.From(stream, encoding: Encoding.UTF8, throwIfBinaryDetected: true); }
            catch (InvalidDataException failure) { throw new ArgumentException("A declared managed source is not compiler-readable text: " + path, failure); }
            sources.Add(new(path, digest, image, text.ToString()));
        }

        while (pending.TryDequeue(out CapturedAssembly? owner))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (AssemblyIdentity reference in owner.references)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Runtime facades can forward optional package types (for example
                // ConfigurationManager) that the plain SDK does not supply. A
                // selected use must still bind in the compiler; a forwarding-only
                // reference is not an implementation dependency to auto-discover.
                if (owner.input.isFramework && owner.optionalForwarders.Contains(reference.Name)) continue;
                if (assemblies.TryGetValue(reference.Name, out CapturedAssembly? known))
                {
                    RequireMatch(reference, known);
                    continue;
                }
                ValidateComponent(reference.Name, "assembly name");
                if (!string.IsNullOrEmpty(reference.CultureName)) ValidateComponent(reference.CultureName, "assembly culture");
                string[] search = new[] { Path.GetDirectoryName(owner.input.path)! }.Concat(roots).Distinct(m_paths).ToArray();
                CapturedAssembly? resolved = null;
                foreach (string directory in search)
                {
                    string cultureRoot = string.IsNullOrEmpty(reference.CultureName) ? directory : Path.Combine(directory, reference.CultureName);
                    foreach (string extension in new[] { ".dll", ".exe" })
                    {
                        string candidate = Path.Combine(cultureRoot, reference.Name + extension);
                        if (File.Exists(candidate))
                        {
                            resolved = AddAssembly(candidate, isFramework: false);
                            RequireMatch(reference, resolved);
                            break;
                        }
                        if (Directory.Exists(candidate)) throw new IOException("An assembly search candidate is a directory: " + candidate);
                        if (absent.Count >= 16384) throw new ArgumentException("Managed dependency resolution exceeds 16384 exact search candidates.");
                        absent.TryAdd(candidate, new(candidate, PhysicalPathResolver.Resolve(candidate, hostPaths)));
                    }
                    if (resolved is not null) break;
                }
                if (resolved is null)
                    throw new FileNotFoundException("The frozen managed metadata closure is incomplete: '" + owner.input.assemblyIdentity
                        + "' requires '" + reference + "'. Declare the matching reference or place it next to an explicitly declared reference; process-loaded assemblies are not consulted.");
            }
        }
        var inputs = new ManagedProjectionInputs
        {
            assemblies = assemblies.Values.Select(value => value.input).OrderBy(value => value.path, StringComparer.Ordinal).ToImmutableArray(),
            sources = sources.ToImmutable(),
            defines = EffectiveDefines(configuration.defines)
        };
        SourceSnapshot snapshot = new(files.Values.OrderBy(value => value.path, StringComparer.Ordinal).ToImmutableArray(), [])
        { absentInputs = absent.Values.OrderBy(value => value.path, StringComparer.Ordinal).ToImmutableArray() };
        snapshot = InputPathSemanticsSnapshot.Capture(snapshot, hostPaths);
        ManagedProjectionInputCapture result = new(inputs, snapshot, hostPaths);
        result.Verify(cancellationToken);
        return result;

        CapturedAssembly AddAssembly(
            string declared,
            bool isFramework
        ) {
            cancellationToken.ThrowIfCancellationRequested();
            string path = Path.GetFullPath(declared);
            CapturedAssembly? existingPath = assemblies.Values.FirstOrDefault(value => m_paths.Equals(value.input.path, path));
            if (existingPath is not null) return existingPath;
            if (!paths.Add(path)) throw new ArgumentException("Managed source and assembly paths must be distinct: " + path);
            if (assemblies.Count >= 1024) throw new ArgumentException("The complete managed metadata closure exceeds 1024 assemblies.");
            (ImmutableArray<byte> image, ContentDigest digest) = Read(path, C_IMAGE_LIMIT, allowEmpty: false);
            try
            {
                using PEReader pe = new(image);
                if (!pe.HasMetadata) throw new BadImageFormatException("The image has no CLI metadata.");
                MetadataReader metadata = pe.GetMetadataReader();
                if (!metadata.IsAssembly) throw new BadImageFormatException("The image is a module rather than an assembly.");
                AssemblyDefinition definition = metadata.GetAssemblyDefinition();
                AssemblyIdentity identity = Identity(metadata, definition.Name, definition.Version, definition.Culture, definition.PublicKey, definition.Flags);
                ValidateComponent(identity.Name, "assembly name");
                if (!string.IsNullOrEmpty(identity.CultureName)) ValidateComponent(identity.CultureName, "assembly culture");
                if (assemblies.TryGetValue(identity.Name, out CapturedAssembly? previous))
                    throw new ArgumentException("Two managed metadata paths declare the same assembly name: " + previous.input.path + " and " + path
                        + ". Supply one unambiguous framework and consumer reference closure.");
                var dependencies = metadata.AssemblyReferences.Select(handle =>
                {
                    AssemblyReference reference = metadata.GetAssemblyReference(handle);
                    return Identity(metadata, reference.Name, reference.Version, reference.Culture, reference.PublicKeyOrToken, reference.Flags);
                }).ToImmutableArray();
                HashSet<AssemblyReferenceHandle> required = metadata.TypeReferences.Select(handle => metadata.GetTypeReference(handle).ResolutionScope)
                    .Where(handle => handle.Kind == HandleKind.AssemblyReference).Select(handle => (AssemblyReferenceHandle)handle).ToHashSet();
                HashSet<string> requiredNames = required.Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var optionalForwarders = metadata.ExportedTypes.Select(handle => metadata.GetExportedType(handle).Implementation)
                    .Where(handle => handle.Kind == HandleKind.AssemblyReference).Select(handle => (AssemblyReferenceHandle)handle)
                    .Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name)).Where(name => !requiredNames.Contains(name))
                    .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
                CapturedAssembly captured = new(new(path, identity.ToString(), digest, image, isFramework), identity, dependencies, optionalForwarders);
                assemblies.Add(identity.Name, captured);
                pending.Enqueue(captured);
                return captured;
            }
            catch (Exception failure) when (failure is BadImageFormatException or InvalidOperationException)
            { throw new ArgumentException("A declared managed reference is not a readable assembly image: " + path, failure); }
        }

        (ImmutableArray<byte> Image, ContentDigest Digest) Read(
            string path,
            int maximum,
            bool allowEmpty
        ) {
            string physical = PhysicalPathResolver.Resolve(path, hostPaths);
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            long length = stream.Length;
            if ((!allowEmpty && length == 0) || length > maximum || checked(total += length) > C_TOTAL_LIMIT)
                throw new ArgumentException("Managed compiler input exceeds its per-file or 512 MiB total byte limit: " + path);
            byte[] bytes = new byte[checked((int)length)];
            int offset = 0;
            while (offset < bytes.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int read = stream.Read(bytes.AsSpan(offset, Math.Min(64 * 1024, bytes.Length - offset)));
                if (read == 0) throw new IOException("A managed input changed length while being captured: " + path);
                offset += read;
            }
            if (stream.ReadByte() != -1 || PhysicalPathResolver.Resolve(path, hostPaths) != physical)
                throw new IOException("A managed input changed while being captured: " + path);
            ContentDigest digest = new(Convert.ToHexStringLower(SHA256.HashData(bytes)));
            files.Add(path, new(path, physical, length, digest));
            return (ImmutableArray.CreateRange(bytes), digest);
        }
    }

    private static string FullPath(
        string path,
        string baseDirectory
    )
        => Path.GetFullPath(string.IsNullOrWhiteSpace(path) ? throw new ArgumentException("A managed input path cannot be empty.") : path, baseDirectory);

    // Static projection symbols describe the declared net10.0 surface; configuration-specific symbols must be supplied explicitly.
    private static ImmutableArray<string> EffectiveDefines(ImmutableArray<string> declared)
        => declared.Concat(new[] { "NET", "NET10_0", "NETCOREAPP", "NETCOREAPP1_0_OR_GREATER", "NETCOREAPP1_1_OR_GREATER",
                "NETCOREAPP2_0_OR_GREATER", "NETCOREAPP2_1_OR_GREATER", "NETCOREAPP2_2_OR_GREATER", "NETCOREAPP3_0_OR_GREATER", "NETCOREAPP3_1_OR_GREATER" })
            .Concat(Enumerable.Range(5, 6).Select(version => "NET" + version + "_0_OR_GREATER"))
            .Distinct(StringComparer.Ordinal).ToImmutableArray();

    private static AssemblyIdentity Identity(
        MetadataReader reader,
        StringHandle name,
        Version version,
        StringHandle culture,
        BlobHandle key,
        AssemblyFlags flags
    )
        => new(reader.GetString(name), version, culture.IsNil ? null : reader.GetString(culture), key.IsNil ? [] : ImmutableArray.CreateRange(reader.GetBlobBytes(key)),
            (flags & AssemblyFlags.PublicKey) != 0, (flags & AssemblyFlags.Retargetable) != 0,
            (flags & AssemblyFlags.WindowsRuntime) != 0 ? AssemblyContentType.WindowsRuntime : AssemblyContentType.Default);

    private static void RequireMatch(
        AssemblyIdentity reference,
        CapturedAssembly actual
    ) {
        if (!StringComparer.OrdinalIgnoreCase.Equals(reference.Name, actual.identity.Name)
            || !StringComparer.OrdinalIgnoreCase.Equals(reference.CultureName, actual.identity.CultureName)
            || !reference.PublicKeyToken.AsSpan().SequenceEqual(actual.identity.PublicKeyToken.AsSpan())
            || reference.ContentType != actual.identity.ContentType
            || (actual.input.isFramework ? reference.Version > actual.identity.Version : reference.Version != actual.identity.Version))
            throw new ArgumentException("A managed metadata dependency has a mismatching identity: required '" + reference
                + "', found '" + actual.identity + "' at " + actual.input.path + ".");
    }

    private static void ValidateComponent(
        string component,
        string label
    ) {
        if (string.IsNullOrWhiteSpace(component) || component is "." or ".." || component.Length > 512 || component.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || component.IndexOfAny(['/', '\\']) >= 0 || Path.GetFileName(component) != component)
            throw new ArgumentException("A metadata dependency contains an invalid " + label + ".");
    }

    private sealed record CapturedAssembly(
        ManagedAssemblyInput input,
        AssemblyIdentity identity,
        ImmutableArray<AssemblyIdentity> references,
        ImmutableHashSet<string> optionalForwarders
    );
}
