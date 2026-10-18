using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using BGCS.Analysis.Interop;
using BGCS.Composition;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;
using BGCS.Infrastructure.Caching;
using BGCS.Infrastructure.IO;
using BGCS.Infrastructure.Workers;

namespace BGCS;

/// <summary>Reuses only primary detached parser facts, with independent input and execution-byte validation.</summary>
internal sealed class FrontendGenerationCache(
    GenerationStageCache? cache,
    string? disabledReason,
    GenerationInputTracker inputs,
    List<GenerationCacheStageResult> evidence,
    List<(GenerationStageCache Cache, ContentDigest Key, ImmutableArray<CachedStageArtifact> Artifacts)> pendingEntries
)
{
    internal Attempt Prepare(
        FrontendRequest request,
        IFrontend frontend,
        CancellationToken cancellationToken
    ) {
        string? reason = cache is null ? disabledReason ?? "Frontend caching is disabled by configuration." : null;
        if (frontend is not FrontendWorkerClient) reason ??= "The selected frontend has no verified reusable parser stage.";
        if (inputs.cacheIneligibilityReasons.Count != 0) reason ??= string.Join(", ", inputs.cacheIneligibilityReasons.Order(StringComparer.Ordinal));
        if (request.arguments.Any(argument => argument.Contains("pch", StringComparison.OrdinalIgnoreCase)
            || argument.Contains("module", StringComparison.OrdinalIgnoreCase) || argument.Contains("vfs", StringComparison.OrdinalIgnoreCase)
            || argument.Contains("profile", StringComparison.OrdinalIgnoreCase)))
            reason ??= "Frontend caching does not yet certify precompiled, modular, virtual-filesystem or profile inputs.";
        ContentDigest key = default;
        if (reason is null)
            try
            {
                WorkerExecutionIdentity execution = ((FrontendWorkerClient)frontend).CaptureExecutionIdentity(cancellationToken);
                inputs.CaptureArgumentInputs(execution.snapshot);
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    stage = "BGCS.PrimaryFrontend",
                    revision = 2,
                    execution.digest,
                    hostImplementation = FileDigest(typeof(BindingHost).Assembly.Location),
                    request = request with { snapshotExcludedDirectories = [] },

                }, WorkerProtocol.jsonOptions);
                key = new(Convert.ToHexStringLower(SHA256.HashData(bytes)));
            }
            catch (Exception failure) when (failure is NotSupportedException or IOException or UnauthorizedAccessException)
            { reason = failure.Message; }
        Attempt attempt = new(request.target.targetId.value, key, reason);
        if (reason is not null)
        {
            evidence.Add(new(attempt.targetId, "primaryFrontend", "disabled", reason));
            return attempt;
        }
        string? miss = null;
        try
        {
            if (cache!.TryRead(key, out ImmutableArray<CachedStageArtifact> artifacts, out miss, cancellationToken))
            {
                if (artifacts.Length != 1 || artifacts[0].descriptor.relativePath != "frontend-result.json" || artifacts[0].descriptor.mediaType != "application/json")
                    throw new InvalidDataException("The frontend cache does not contain exactly one detached result.");
                FrontendResult parsed = JsonSerializer.Deserialize<FrontendResult>(artifacts[0].content.Span, WorkerProtocol.jsonOptions)
                    ?? throw new InvalidDataException("The cached frontend result is empty.");
                if (parsed.module is null || parsed.snapshot is null || parsed.module.targetId != request.target.targetId.value
                    || parsed.module.triple != request.target.triple || parsed.module.language != request.language)
                    throw new InvalidDataException("The cached frontend result changed its target or language identity.");
                parsed = parsed with { snapshot = parsed.snapshot with { excludedDirectories = request.snapshotExcludedDirectories } };
                miss = parsed.executionInputs is null ? "The cached frontend lacks a complete parser execution-input snapshot."
                    : SourceSnapshotVerifier.GetMismatch(parsed.executionInputs, cancellationToken)
                    ?? SourceSnapshotVerifier.GetMismatch(parsed.snapshot, cancellationToken);
                if (miss is null && !new NativeModuleVerifier().Validate(parsed.module).Any(diagnostic => diagnostic.isFailure)
                    && !parsed.module.diagnostics.Any(diagnostic => diagnostic.isFailure))
                {
                    miss = VolatileReason(request, parsed, cancellationToken);
                    if (miss is null) attempt.result = parsed;
                }
                else miss ??= "The cached native graph failed its independent contract verification.";
            }
        }
        catch (Exception failure) when (failure is JsonException or InvalidDataException or ArgumentException)
        { miss = failure.Message; }
        evidence.Add(new(attempt.targetId, "primaryFrontend", attempt.result is null ? "miss" : "hit", miss));
        return attempt;

        static string FileDigest(string path)
        {
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexStringLower(SHA256.HashData(stream));
        }
    }

    internal void Capture(
        Attempt attempt,
        FrontendRequest request,
        FrontendResult parsed,
        CancellationToken cancellationToken
    ) {
        if (cache is null || attempt.reason is not null || attempt.result is not null) return;
        string? volatileReason = parsed.executionInputs is null ? "The frontend did not provide a complete parser execution-input snapshot."
            : VolatileReason(request, parsed, cancellationToken);
        if (volatileReason is not null)
        {
            int index = evidence.FindLastIndex(row => row.targetId == attempt.targetId && row.stage == "primaryFrontend");
            if (index >= 0) evidence[index] = new(attempt.targetId, "primaryFrontend", "disabled", volatileReason);
            return;
        }
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(parsed, WorkerProtocol.jsonOptions);
        if (bytes.Length > cache.maximumEntryBytes) return;
        ArtifactDescriptor descriptor = new("frontend-result.json", "application/json", new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(bytes))));
        pendingEntries.Add((cache, attempt.key, [new(descriptor, bytes)]));
    }

    private static string? VolatileReason(
        FrontendRequest request,
        FrontendResult parsed,
        CancellationToken cancellationToken
    ) {
        if (ContainsVolatile(request.preamble) || ContainsVolatile(request.postamble)
            || request.defines.Concat(request.target.toolchain.defines).Concat(request.arguments).Any(ContainsVolatile)) return "Volatile or token-pasted preprocessing inputs require a fresh parse.";
        foreach (var input in parsed.snapshot.files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (input.length > 67108864) return "A parser source exceeds the bounded volatility inspection limit.";
            string content = File.ReadAllText(input.path, Encoding.UTF8);
            if (ContainsVolatile(content)) return "Volatile or token-pasted preprocessing inputs require a fresh parse: " + input.path;
        }
        return null;

    }

    private static bool ContainsVolatile(string value)
    {
        value = value.Replace("\\\r\n", "", StringComparison.Ordinal).Replace("\\\n", "", StringComparison.Ordinal);
        return value.Contains("__DATE__", StringComparison.Ordinal) || value.Contains("__TIME__", StringComparison.Ordinal)
            || value.Contains("__TIMESTAMP__", StringComparison.Ordinal) || value.Contains("__has_include", StringComparison.Ordinal)
            || value.Contains("##", StringComparison.Ordinal) || value.Contains("%:%:", StringComparison.Ordinal);
    }

    internal sealed class Attempt(
        string targetId,
        ContentDigest key,
        string? reason
    )
    {
        internal string targetId { get; } = targetId;
        internal ContentDigest key { get; } = key;
        internal string? reason { get; } = reason;
        internal FrontendResult? result { get; set; }
    }
}
