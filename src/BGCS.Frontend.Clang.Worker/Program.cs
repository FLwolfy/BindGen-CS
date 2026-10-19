using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using BGCS.Frontend.Clang;
using BGCS.Frontend.Clang.Worker;
using BGCS.Infrastructure.Workers;
using BGCS.Contracts.Requests;

Stream input = Console.OpenStandardInput();
Stream output = Console.OpenStandardOutput();
Console.SetOut(Console.Error);
string build = typeof(FrontendWorkerClient).Assembly.GetName().Version?.ToString() ?? string.Empty;
await WorkerProtocol.WriteAsync(output,
    new WorkerHandshake(WorkerProtocol.C_REVISION, BGCS.Contracts.Versioning.ModelRevision.C_NATIVE, build, WorkerProtocol.C_PARSER_IDENTITY, RuntimeInformation.RuntimeIdentifier),
    CancellationToken.None);
WorkerParseRequest envelope = await WorkerProtocol.ReadAsync<WorkerParseRequest>(input, CancellationToken.None);
try
{
    if (!Enum.IsDefined(envelope.operation)) throw new InvalidDataException("The worker operation is unknown.");
    if (!Enum.IsDefined(envelope.request.purpose)) throw new InvalidDataException("The frontend request purpose is unknown.");
    await using WorkerPluginHost plugins = await WorkerPluginHost.CreateAsync(envelope, CancellationToken.None);
    var capture = plugins.Capture(CancellationToken.None);
    if (envelope.expectedPluginCapture is { } expected) WorkerPluginHost.RequireSame(expected, capture);
    FrontendResult? module = null;
    if (envelope.operation == WorkerOperationKind.Parse)
    {
        module = await new ClangFrontend().ParseAsync(envelope.request, plugins.contributors, CancellationToken.None);
        WorkerPluginHost.RequireSame(capture, plugins.Capture(CancellationToken.None));
        if (!envelope.pluginActivations.IsEmpty)
            module = module with { executionInputs = WorkerPluginHost.Merge(new[] { module.executionInputs ?? new([], []), capture.inputs }) };
    }
    await plugins.DisposeAsync();
    await WorkerProtocol.WriteAsync(output, new WorkerResponse(envelope.requestId, module, null) { pluginCapture = envelope.pluginActivations.IsEmpty ? null : capture }, CancellationToken.None);
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    string summary = exception.Message;
    int newline = summary.IndexOfAny(['\r', '\n']);
    if (newline >= 0) summary = summary[..newline];
    if (summary.Length > 2048) summary = summary[..2048] + " [truncated]";
    await WorkerProtocol.WriteAsync(output, new WorkerResponse(envelope.requestId, null, summary), CancellationToken.None);
    Environment.ExitCode = 1;
}
