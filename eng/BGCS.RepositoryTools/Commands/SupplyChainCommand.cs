using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BGCS.RepositoryTools.Commands;

internal static class SupplyChainCommand
{
    private const string C_DEFAULTREPOSITORY = "https://github.com/FLwolfy/BindGen-CS";
    private const string C_DEFAULTBUILDER = "https://github.com/FLwolfy/BindGen-CS/.github/workflows/publish-bgcs-runtime-nuget.yml";
    internal static int Run(
        string[] args,
        string workingDirectory,
        TextWriter output,
        TextWriter error
    ) {
        try
        {
            Options options = Parse(args);
            string artifactRoot = Path.GetFullPath(options.artifactRoot ?? "artifacts/nuget", workingDirectory);
            string outputRoot = Path.GetFullPath(options.outputRoot ?? "artifacts/supply-chain", workingDirectory);
            if (!Directory.Exists(artifactRoot))
                throw new DirectoryNotFoundException($"Artifact directory was not found: {artifactRoot}");
            string sbomPath = Path.Combine(outputRoot, "sbom.spdx.json");
            string provenancePath = Path.Combine(outputRoot, "provenance.slsa.json");
            Artifact[] artifacts = Directory.GetFiles(artifactRoot, "*", SearchOption.AllDirectories).Where(path => !string.Equals(Path.GetFullPath(path), sbomPath, StringComparison.OrdinalIgnoreCase) && !string.Equals(Path.GetFullPath(path), provenancePath, StringComparison.OrdinalIgnoreCase)).Select(path => CreateArtifact(artifactRoot, path)).OrderBy(artifact => artifact.name, StringComparer.Ordinal).ToArray();
            if (artifacts.Length == 0)
                throw new InvalidDataException($"Artifact directory '{artifactRoot}' contains no files.");
            DateTimeOffset timestamp = ResolveTimestamp(options.timestamp);
            string revision = options.revision ?? Environment.GetEnvironmentVariable("SOURCE_REVISION") ?? "unknown";
            revision = VerifyAcceptedPackages(artifactRoot, artifacts, revision);
            string repository = options.repository ?? global::BGCS.RepositoryTools.Commands.SupplyChainCommand.C_DEFAULTREPOSITORY;
            string aggregate = ComputeAggregateDigest(artifacts);
            DeliveredDependencyGraph graph = new();
            foreach (Artifact artifact in artifacts)
                graph.AddArtifact(Path.Combine(artifactRoot, artifact.name.Replace('/', Path.DirectorySeparatorChar)), artifact.name, artifact.sha256);
            graph.Complete();
            foreach (Artifact artifact in artifacts)
                if (CreateArtifact(artifactRoot, Path.Combine(artifactRoot, artifact.name.Replace('/', Path.DirectorySeparatorChar))).sha256 != artifact.sha256)
                    throw new InvalidDataException("A delivered artifact changed while its dependency evidence was being read: " + artifact.name);
            Directory.CreateDirectory(outputRoot);
            WriteJson(sbomPath, CreateSpdx(graph, timestamp, aggregate, repository));
            WriteJson(provenancePath, CreateProvenance(artifacts, graph, timestamp, aggregate, repository, revision, options.builderId ?? global::BGCS.RepositoryTools.Commands.SupplyChainCommand.C_DEFAULTBUILDER));
            output.WriteLine($"SPDX SBOM: {sbomPath}");
            output.WriteLine($"SLSA provenance: {provenancePath}");
            output.WriteLine($"Covered {artifacts.Length} artifact(s); aggregate SHA-256 {aggregate}.");
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or InvalidDataException or FormatException
            or JsonException or KeyNotFoundException or InvalidOperationException or System.Xml.XmlException)
        {
            error.WriteLine($"error: {exception.Message}");
            error.WriteLine("Run 'bindgen-cs --help' for usage.");
            return 2;
        }
    }

    private static object CreateSpdx(
        DeliveredDependencyGraph graph,
        DateTimeOffset timestamp,
        string aggregate,
        string repository
    ) => new
    {
        spdxVersion = "SPDX-2.3",
        dataLicense = "CC0-1.0",
        SPDXID = "SPDXRef-DOCUMENT",
        name = "BindGen-CS release artifacts",
        documentNamespace = $"{repository.TrimEnd('/')}/sbom/{aggregate}",
        creationInfo = new
        {
            created = timestamp.ToUniversalTime().ToString("O"),
            creators = new[]
            {
                "Tool: BindGen-CS"
            },
            licenseListVersion = "3.25"
        },
        packages = graph.Packages,
        files = graph.Files,
        relationships = graph.Relationships,
        hasExtractedLicensingInfos = graph.ExtractedLicenses
    };
    private static object CreateProvenance(
        IReadOnlyList<Artifact> artifacts,
        DeliveredDependencyGraph graph,
        DateTimeOffset timestamp,
        string aggregate,
        string repository,
        string revision,
        string builderId
    ) => new
    {
        _type = "https://in-toto.io/Statement/v1",
        subject = artifacts.Select(artifact => new { name = artifact.name, digest = new { sha256 = artifact.sha256 } }).ToArray(),
        predicateType = "https://slsa.dev/provenance/v1",
        predicate = new
        {
            buildDefinition = new
            {
                buildType = "https://github.com/FLwolfy/BindGen-CS/buildtypes/dotnet-pack/v1",
                externalParameters = new
                {
                    configuration = "Release",
                    deterministic = true
                },
                internalParameters = new
                {
                    aggregateSha256 = aggregate
                },
                resolvedDependencies = new object[]
                {
                    new
                    {
                        uri = $"git+{repository}.git",
                        digest = new
                        {
                            gitCommit = revision
                        }
                    }
                }.Concat(graph.Materials).ToArray()
            },
            runDetails = new
            {
                builder = new
                {
                    id = builderId
                },
                metadata = new
                {
                    invocationId = aggregate,
                    startedOn = timestamp.ToUniversalTime().ToString("O"),
                    finishedOn = timestamp.ToUniversalTime().ToString("O")
                },
                byproducts = Array.Empty<object>()
            }
        }
    };
    private static Artifact CreateArtifact(
        string root,
        string path
    ) {
        string name = Path.GetRelativePath(root, path).Replace('\\', '/');
        using FileStream stream = File.OpenRead(path);
        return new(name, Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
    }

    private static string VerifyAcceptedPackages(
        string root,
        IReadOnlyList<Artifact> artifacts,
        string revision
    ) {
        Artifact[] archives = artifacts.Where(artifact => artifact.name.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)
            || artifact.name.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (archives.Length == 0)
            return revision;
        string manifestPath = Path.Combine(root, "package-verification.json");
        if (!File.Exists(manifestPath) || new FileInfo(manifestPath).Length > 16 * 1024 * 1024)
            throw new InvalidDataException("NuGet SBOM generation requires its bounded accepted package-verification.json manifest.");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        JsonElement manifest = document.RootElement;
        string acceptedRevision = manifest.GetProperty("sourceRevision").GetString() ?? "";
        string content = manifest.GetProperty("checkedOutSourceSha256").GetString() ?? "";
        JsonElement after = manifest.GetProperty("sourceAtCompletion");
        if (manifest.GetProperty("schemaVersion").GetInt32() != 1 || manifest.GetProperty("completeProductSet").ValueKind != JsonValueKind.True
            || manifest.GetProperty("sourceRevisionStable").ValueKind != JsonValueKind.True || manifest.GetProperty("sourceBytesStable").ValueKind != JsonValueKind.True
            || manifest.GetProperty("sourceWorktreeDirty").ValueKind != JsonValueKind.False || after.GetProperty("sourceWorktreeDirty").ValueKind != JsonValueKind.False
            || acceptedRevision.Length is not (40 or 64) || !acceptedRevision.All(char.IsAsciiHexDigit)
            || content.Length != 64 || !content.All(char.IsAsciiHexDigit)
            || after.GetProperty("sourceRevision").GetString() != acceptedRevision || after.GetProperty("checkedOutSourceSha256").GetString() != content
            || revision != "unknown" && revision != acceptedRevision)
            throw new InvalidDataException("The accepted package inventory does not describe a complete stable build of this requested source revision.");
        Dictionary<string, Artifact> actual = archives.ToDictionary(artifact => artifact.name, StringComparer.Ordinal);
        HashSet<string> matched = new(StringComparer.Ordinal);
        foreach (JsonElement item in manifest.GetProperty("packages").EnumerateArray())
        {
            string name = item.GetProperty("file").GetString() ?? "";
            if (!matched.Add(name) || !actual.TryGetValue(name, out Artifact? archive)
                || item.GetProperty("sha256").GetString() != archive.sha256
                || item.GetProperty("bytes").GetInt64() != new FileInfo(Path.Combine(root, name)).Length)
                throw new InvalidDataException("The accepted package inventory contradicts the exact delivered archive/hash set: " + name);
        }
        if (!matched.SetEquals(actual.Keys))
            throw new InvalidDataException("The accepted package inventory omits a delivered NuGet archive.");
        return acceptedRevision;
    }

    private static string ComputeAggregateDigest(IEnumerable<Artifact> artifacts)
    {
        StringBuilder canonical = new();
        foreach (Artifact artifact in artifacts)
            canonical.Append(artifact.name).Append('\0').Append(artifact.sha256).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
    }

    private static DateTimeOffset ResolveTimestamp(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return DateTimeOffset.Parse(configured, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);
        string? epoch = Environment.GetEnvironmentVariable("SOURCE_DATE_EPOCH");
        return long.TryParse(epoch, out long seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds) : DateTimeOffset.UtcNow;
    }

    private static void WriteJson(
        string path,
        object value
    ) => File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    }) + Environment.NewLine);
    private static Options Parse(string[] args)
    {
        string? artifactRoot = null;
        string? outputRoot = null;
        string? repository = null;
        string? revision = null;
        string? builderId = null;
        string? timestamp = null;
        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            if (argument is "--output" or "-o")
                outputRoot = ReadValue(args, ref index, argument);
            else if (argument == "--repository")
                repository = ReadValue(args, ref index, argument);
            else if (argument == "--revision")
                revision = ReadValue(args, ref index, argument);
            else if (argument == "--builder-id")
                builderId = ReadValue(args, ref index, argument);
            else if (argument == "--timestamp")
                timestamp = ReadValue(args, ref index, argument);
            else if (argument.StartsWith("-", StringComparison.Ordinal))
                throw new ArgumentException($"Unknown supply-chain option '{argument}'.");
            else if (artifactRoot == null)
                artifactRoot = argument;
            else
                throw new ArgumentException("supply-chain accepts at most one artifact directory.");
        }

        return new(artifactRoot, outputRoot, repository, revision, builderId, timestamp);
    }

    private static string ReadValue(
        string[] args,
        ref int index,
        string option
    ) {
        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
            throw new ArgumentException($"Option '{option}' requires a value.");
        return args[index];
    }

    private sealed record Artifact(
        string name,
        string sha256
    );
    private sealed record Options(
        string? artifactRoot,
        string? outputRoot,
        string? repository,
        string? revision,
        string? builderId,
        string? timestamp
    );
}
