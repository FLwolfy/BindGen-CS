using System;
using BGCS.Core.Hosting;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Infrastructure.IO;
using BGCS.Configuration;

namespace BGCS;

/// <summary>Loads only strict versioned workspace inputs and captures every project's own path base.</summary>
public sealed class WorkspaceLoader
{
    private readonly IHostPathService m_paths;
    /// <summary>Uses host facilities selected before reading a workspace or its project manifests.</summary>
    /// <param name="paths">The selected host path service.</param>
    public WorkspaceLoader(IHostPathService paths) => m_paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private const int C_MAXIMUMBYTES = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions s_options = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        MaxDepth = 64
    };

    /// <summary>Loads and validates all configuration closures before any project starts.</summary>
    /// <param name="path">The local workspace input.</param>
    /// <param name="cancellationToken">Cancels workspace and project configuration reads.</param>
    /// <returns>A frozen dependency graph with exact local input identities.</returns>
    public BindingWorkspace Load(
        string path,
        CancellationToken cancellationToken = default
    )
        => LoadAsync(path, cancellationToken).GetAwaiter().GetResult();

    /// <summary>Loads every project's immutable configuration closure asynchronously before execution begins.</summary>
    /// <param name="path">The local workspace document.</param>
    /// <param name="cancellationToken">Cancels local configuration reads.</param>
    /// <returns>The validated dependency graph with local project provenance.</returns>
    public async Task<BindingWorkspace> LoadAsync(
        string path,
        CancellationToken cancellationToken = default
    ) {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string absolute = Path.GetFullPath(path);
        string resolved = PhysicalPathResolver.Resolve(absolute, m_paths);
        byte[] bytes;
        await using (FileStream stream = new(absolute, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true))
        {
            if (stream.Length > C_MAXIMUMBYTES) throw new IOException("A workspace input exceeds the 4 MiB limit.");
            bytes = new byte[checked((int)stream.Length)];
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            if (stream.ReadByte() != -1) throw new IOException("A workspace input changed while it was being read.");
        }
        RejectDuplicateFields(bytes);
        WorkspaceConfiguration document = JsonSerializer.Deserialize<WorkspaceConfiguration>(bytes, s_options) ?? throw new JsonException("A workspace document cannot be null.");
        if (document.schemaVersion != 1) throw new JsonException("Only workspace schemaVersion 1 is supported.");
        if (document.projects.IsDefaultOrEmpty) throw new JsonException("A workspace requires at least one project.");
        string pathBase = Path.GetDirectoryName(absolute)!;
        InputDependency input = new(absolute, resolved, bytes.LongLength, new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(bytes))));
        ImmutableArray<WorkspaceProject>.Builder projects = ImmutableArray.CreateBuilder<WorkspaceProject>();
        foreach (WorkspaceProjectConfiguration project in document.projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(project);
            ArgumentException.ThrowIfNullOrWhiteSpace(project.configuration);
            GenerationRequest request = await GenerationRequest.FromFile(Path.GetFullPath(project.configuration, pathBase), project.profile)
                .MaterializeAsync(m_paths, cancellationToken).ConfigureAwait(false);
            if (!project.targets.IsDefaultOrEmpty)
            {
                if (project.targets.Distinct(StringComparer.Ordinal).Count() != project.targets.Length || project.targets.Any(string.IsNullOrWhiteSpace))
                    throw new JsonException("Workspace target selections must be unique nonempty identities.");
                foreach (string target in project.targets)
                    if (!request.configuration.targets.Any(configured => configured.id == target)) throw new JsonException($"Project '{project.id}' selects an undeclared target '{target}'.");
                request = request.WithConfiguration(request.configuration with { targets = request.configuration.targets.Where(target => project.targets.Contains(target.id, StringComparer.Ordinal)).ToImmutableArray() });
            }
            if (project.targets.IsDefault) throw new JsonException("Workspace targets cannot be an uninitialized collection.");
            projects.Add(new(project.id, request.WithAdditionalInputs(InputPathSemanticsSnapshot.Capture(new([input], []), m_paths), m_paths), project.dependencies));
        }
        return new(projects.ToImmutable(), m_paths, document.parallelism);
    }

    private static void RejectDuplicateFields(byte[] bytes)
    {
        using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
        Visit(document.RootElement);
        static void Visit(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                System.Collections.Generic.HashSet<string> names = new(StringComparer.Ordinal);
                foreach (JsonProperty property in value.EnumerateObject())
                {
                    if (!names.Add(property.Name)) throw new JsonException($"Duplicate workspace field '{property.Name}'.");
                    Visit(property.Value);
                }
            }
            else if (value.ValueKind == JsonValueKind.Array) foreach (JsonElement item in value.EnumerateArray()) Visit(item);
        }
    }

}
