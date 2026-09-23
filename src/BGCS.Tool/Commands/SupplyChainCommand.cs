using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BGCS.Tool.Commands;

internal static class SupplyChainCommand
{
    private const string DefaultRepository = "https://github.com/FLwolfy/BindGen-CS";
    private const string DefaultBuilder = "https://github.com/FLwolfy/BindGen-CS/.github/workflows/release";

    internal static int Run(string[] args, string workingDirectory, TextWriter output, TextWriter error)
    {
        try
        {
            Options options = Parse(args);
            string artifactRoot = Path.GetFullPath(options.ArtifactRoot ?? "artifacts/nuget", workingDirectory);
            string outputRoot = Path.GetFullPath(options.OutputRoot ?? "artifacts/supply-chain", workingDirectory);
            if (!Directory.Exists(artifactRoot))
                throw new DirectoryNotFoundException($"Artifact directory was not found: {artifactRoot}");
            string sbomPath = Path.Combine(outputRoot, "sbom.spdx.json");
            string provenancePath = Path.Combine(outputRoot, "provenance.slsa.json");
            Artifact[] artifacts = Directory.GetFiles(artifactRoot, "*", SearchOption.AllDirectories)
                .Where(path => !string.Equals(Path.GetFullPath(path), sbomPath, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(Path.GetFullPath(path), provenancePath, StringComparison.OrdinalIgnoreCase))
                .Select(path => CreateArtifact(artifactRoot, path))
                .OrderBy(artifact => artifact.Name, StringComparer.Ordinal)
                .ToArray();
            if (artifacts.Length == 0)
                throw new InvalidDataException($"Artifact directory '{artifactRoot}' contains no files.");

            DateTimeOffset timestamp = ResolveTimestamp(options.Timestamp);
            string revision = options.Revision ?? Environment.GetEnvironmentVariable("SOURCE_REVISION") ?? "unknown";
            string repository = options.Repository ?? DefaultRepository;
            string aggregate = ComputeAggregateDigest(artifacts);
            Directory.CreateDirectory(outputRoot);
            WriteJson(sbomPath, CreateSpdx(artifacts, timestamp, aggregate, repository));
            WriteJson(provenancePath, CreateProvenance(artifacts, timestamp, aggregate, repository, revision,
                options.BuilderId ?? DefaultBuilder));
            output.WriteLine($"SPDX SBOM: {sbomPath}");
            output.WriteLine($"SLSA provenance: {provenancePath}");
            output.WriteLine($"Covered {artifacts.Length} artifact(s); aggregate SHA-256 {aggregate}.");
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or InvalidDataException or FormatException)
        {
            error.WriteLine($"error: {exception.Message}");
            error.WriteLine("Run 'bindgen-cs --help' for usage.");
            return 2;
        }
    }

    private static object CreateSpdx(IReadOnlyList<Artifact> artifacts, DateTimeOffset timestamp,
        string aggregate, string repository) => new
    {
        spdxVersion = "SPDX-2.3",
        dataLicense = "CC0-1.0",
        SPDXID = "SPDXRef-DOCUMENT",
        name = "BindGen-CS release artifacts",
        documentNamespace = $"{repository.TrimEnd('/')}/sbom/{aggregate}",
        creationInfo = new
        {
            created = timestamp.ToUniversalTime().ToString("O"),
            creators = new[] { "Tool: BindGen-CS" },
            licenseListVersion = "3.25"
        },
        documentDescribes = artifacts.Select((_, index) => $"SPDXRef-Package-{index + 1}").ToArray(),
        packages = artifacts.Select((artifact, index) => new
        {
            SPDXID = $"SPDXRef-Package-{index + 1}",
            name = artifact.Name,
            downloadLocation = "NOASSERTION",
            filesAnalyzed = false,
            checksums = new[] { new { algorithm = "SHA256", checksumValue = artifact.Sha256 } },
            licenseConcluded = "NOASSERTION",
            licenseDeclared = "NOASSERTION",
            copyrightText = "NOASSERTION"
        }).ToArray()
    };

    private static object CreateProvenance(IReadOnlyList<Artifact> artifacts, DateTimeOffset timestamp,
        string aggregate, string repository, string revision, string builderId) => new
    {
        _type = "https://in-toto.io/Statement/v1",
        subject = artifacts.Select(artifact => new
        {
            name = artifact.Name,
            digest = new { sha256 = artifact.Sha256 }
        }).ToArray(),
        predicateType = "https://slsa.dev/provenance/v1",
        predicate = new
        {
            buildDefinition = new
            {
                buildType = "https://github.com/FLwolfy/BindGen-CS/buildtypes/dotnet-pack/v1",
                externalParameters = new { configuration = "Release", deterministic = true },
                internalParameters = new { aggregateSha256 = aggregate },
                resolvedDependencies = new[]
                {
                    new
                    {
                        uri = $"git+{repository}.git",
                        digest = new { gitCommit = revision }
                    }
                }
            },
            runDetails = new
            {
                builder = new { id = builderId },
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

    private static Artifact CreateArtifact(string root, string path)
    {
        string name = Path.GetRelativePath(root, path).Replace('\\', '/');
        using FileStream stream = File.OpenRead(path);
        return new(name, Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
    }

    private static string ComputeAggregateDigest(IEnumerable<Artifact> artifacts)
    {
        StringBuilder canonical = new();
        foreach (Artifact artifact in artifacts)
            canonical.Append(artifact.Name).Append('\0').Append(artifact.Sha256).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
    }

    private static DateTimeOffset ResolveTimestamp(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return DateTimeOffset.Parse(configured, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);
        string? epoch = Environment.GetEnvironmentVariable("SOURCE_DATE_EPOCH");
        return long.TryParse(epoch, out long seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : DateTimeOffset.UtcNow;
    }

    private static void WriteJson(string path, object value) => File.WriteAllText(path,
        JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);

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

    private static string ReadValue(string[] args, ref int index, string option)
    {
        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
            throw new ArgumentException($"Option '{option}' requires a value.");
        return args[index];
    }

    private sealed record Artifact(string Name, string Sha256);
    private sealed record Options(string? ArtifactRoot, string? OutputRoot, string? Repository,
        string? Revision, string? BuilderId, string? Timestamp);
}
