using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Infrastructure.Workers;

Stream input = Console.OpenStandardInput();
Stream output = Console.OpenStandardOutput();
if (args.Length > 0 && args[0] == "--child")
{
    await Task.Delay(Timeout.Infinite);
    return;
}
string version = typeof(FrontendWorkerClient).Assembly.GetName().Version!.ToString();
await WorkerProtocol.WriteAsync(output, new WorkerHandshake(WorkerProtocol.C_REVISION, 1, version,
    WorkerProtocol.C_PARSER_IDENTITY, RuntimeInformation.RuntimeIdentifier), CancellationToken.None);
WorkerParseRequest envelope = await WorkerProtocol.ReadAsync<WorkerParseRequest>(input, CancellationToken.None);
switch (envelope.request.headers[0])
{
    case "exit":
        Environment.Exit(137);
        break;
    case "native-abort":
        AbortFromNativeRuntime();
        throw new InvalidOperationException("Native abort unexpectedly returned.");
    case "hang":
        PublishProcessId(envelope.request.headers[1], Environment.ProcessId);
        await Task.Delay(Timeout.Infinite);
        break;
    case "child":
        ProcessStartInfo start = new(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet")
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--child");
        using (Process child = Process.Start(start)!)
        {
            PublishProcessId(envelope.request.headers[1], child.Id);
            await Task.Delay(Timeout.Infinite);
        }
        break;
    case "wrong-id":
        await WorkerProtocol.WriteAsync(output, new WorkerResponse("another-request", null, "invalid"), CancellationToken.None);
        break;
    case "oversized":
        byte[] prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, WorkerProtocol.C_MAXIMUM_FRAME_BYTES + 1);
        await output.WriteAsync(prefix);
        break;
    case "partial":
        await output.WriteAsync(new byte[] { 20, 0, 0, 0, (byte)'{' });
        break;
    case "invalid-json":
        await output.WriteAsync(new byte[] { 1, 0, 0, 0, (byte)'{' });
        break;
    case "stderr-flood":
        await Console.Error.WriteAsync(new string('x', 1024 * 1024));
        await Console.Error.WriteLineAsync("LAST_DIAGNOSTIC");
        await WorkerProtocol.WriteAsync(output, new WorkerResponse(envelope.requestId, null, null), CancellationToken.None);
        break;
}
await output.FlushAsync();

static void PublishProcessId(string path, int processId)
{
    File.WriteAllText(path + ".tmp", processId.ToString());
    File.Move(path + ".tmp", path);
}

static unsafe void AbortFromNativeRuntime()
{
    nint runtime = NativeLibrary.Load(OperatingSystem.IsWindows() ? "ucrtbase.dll"
        : OperatingSystem.IsMacOS() ? "/usr/lib/libSystem.B.dylib" : "libc.so.6");
    if (OperatingSystem.IsWindows())
    {
        // Suppress CRT reporting and Watson dumps in this deliberately faulting test process.
        var behavior = (delegate* unmanaged[Cdecl]<uint, uint, uint>)NativeLibrary.GetExport(runtime, "_set_abort_behavior");
        _ = behavior(0, 3);
    }
    else
    {
        // RLIMIT_CORE = 4 on the certified Linux and Darwin test hosts. Avoid large crash dumps.
        ulong* limits = stackalloc ulong[2] { 0, 0 };
        var limit = (delegate* unmanaged[Cdecl]<int, ulong*, int>)NativeLibrary.GetExport(runtime, "setrlimit");
        if (limit(4, limits) != 0) throw new InvalidOperationException("Could not disable test process core dumps.");
    }
    var abort = (delegate* unmanaged[Cdecl]<void>)NativeLibrary.GetExport(runtime, "abort");
    Console.Error.WriteLine("NATIVE_ABORT_ENTERED");
    Console.Error.Flush();
    abort();
}
