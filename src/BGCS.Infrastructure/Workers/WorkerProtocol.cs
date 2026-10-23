using System;
using System.Buffers.Binary;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Requests;

namespace BGCS.Infrastructure.Workers;

/// <summary>Defines the bounded, length-framed protocol shared by parser clients and workers.</summary>
public static class WorkerProtocol
{
    /// <summary>The only accepted protocol revision.</summary>
    public const int C_REVISION = BGCS.Core.Versioning.ModelRevision.C_FRONTEND_PROTOCOL;
    /// <summary>The maximum JSON payload size in bytes.</summary>
    public const int C_MAXIMUM_FRAME_BYTES = 256 * 1024 * 1024;

    /// <summary>Gets the shared JSON options for immutable worker contracts.</summary>
    public static JsonSerializerOptions jsonOptions { get; } = CreateJsonOptions();

    /// <summary>Writes one bounded frame without closing the underlying stream.</summary>
    /// <typeparam name="T">The message contract.</typeparam>
    /// <param name="stream">The protocol output stream.</param>
    /// <param name="message">The message to serialize.</param>
    /// <param name="cancellationToken">Cancels serialization transport.</param>
    /// <returns>The frame-write operation.</returns>
    /// <exception cref="InvalidDataException">The payload exceeds the protocol limit.</exception>
    public static async ValueTask WriteAsync<T>(
        Stream stream,
        T message,
        CancellationToken cancellationToken
    ) {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(message, jsonOptions);
        if (payload.Length == 0 || payload.Length > C_MAXIMUM_FRAME_BYTES)
            throw new InvalidDataException("The worker frame exceeds the allowed payload size.");
        byte[] prefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, payload.Length);
        await stream.WriteAsync(prefix, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads and validates one complete frame.</summary>
    /// <typeparam name="T">The expected message contract.</typeparam>
    /// <param name="stream">The protocol input stream.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The deserialized message.</returns>
    /// <exception cref="InvalidDataException">The frame size or payload is invalid.</exception>
    /// <exception cref="EndOfStreamException">The worker closes an incomplete frame.</exception>
    public static async ValueTask<T> ReadAsync<T>(
        Stream stream,
        CancellationToken cancellationToken
    ) {
        byte[] prefix = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(prefix, cancellationToken).ConfigureAwait(false);
        int length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length <= 0 || length > C_MAXIMUM_FRAME_BYTES)
            throw new InvalidDataException("The worker frame declares an invalid payload size.");
        byte[] payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(payload, jsonOptions)
            ?? throw new InvalidDataException("The worker frame contains a null message.");
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            MaxDepth = 128,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new NativeToolchainJsonConverter());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}

/// <summary>Identifies the worker's protocol, model and build versions before accepting input.</summary>
/// <param name="protocolRevision">The protocol revision.</param>
/// <param name="modelRevision">The native model revision.</param>
/// <param name="buildVersion">The worker product build version.</param>
/// <param name="parserIdentity">The pinned parser identity.</param>
/// <param name="hostRid">The actual worker host RID.</param>
public sealed record WorkerHandshake(
    int protocolRevision,
    int modelRevision,
    string buildVersion,
    string parserIdentity,
    string hostRid
);

/// <summary>Returns a detached native graph or a structured worker failure for one request.</summary>
/// <param name="requestId">The request identity.</param>
/// <param name="result">The detached native result, or null on failure.</param>
/// <param name="error">The failure description, or null on success.</param>
public sealed record WorkerResponse(
    string requestId,
    FrontendResult? result,
    string? error
)
{
    /// <summary>Gets complete worker-only extension state captured independently in this process.</summary>
    public BGCS.Core.Plugins.WorkerPluginCapture? pluginCapture { get; init; }
}
