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
if (args.Length > 0 && args[0] == "--exit-probe") return;
if (args.Length > 0 && args[0] == "--zombie-probe")
{
    await HoldExitedNativeChildAsync(args[1]);
    return;
}
if (args.Length > 0 && args[0] == "--child")
{
    await Task.Delay(Timeout.Infinite);
    return;
}
string version = typeof(FrontendWorkerClient).Assembly.GetName().Version!.ToString();
await WorkerProtocol.WriteAsync(output, new WorkerHandshake(WorkerProtocol.C_REVISION, BGCS.Contracts.Versioning.ModelRevision.C_NATIVE, version,
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
        PublishProcessId(envelope.request.headers[1] + ".worker", Environment.ProcessId);
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
        await WorkerProtocol.WriteAsync(output, new WorkerResponse(envelope.requestId, null, "fixture failure summary"), CancellationToken.None);
        break;
}
await output.FlushAsync();

static void PublishProcessId(string path, int processId)
{
    File.WriteAllText(path + ".tmp", processId.ToString());
    File.Move(path + ".tmp", path);
}

static async Task HoldExitedNativeChildAsync(string marker)
{
    if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The zombie probe requires Linux /proc.");
    int child = SpawnExitedNativeChild();
    try
    {
        PublishProcessId(marker, child);
        _ = await Console.In.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30));
    }
    finally { ReapNativeChild(child); }
}

static unsafe int SpawnExitedNativeChild()
{
    // Use posix_spawn rather than fork: no managed code executes in a forked
    // child, and this native child is intentionally outside Process's reaper.
    nint library = NativeLibrary.Load("libc.so.6");
    try
    {
        var spawn = (delegate* unmanaged[Cdecl]<int*, byte*, void*, void*, byte**, byte**, int>)NativeLibrary.GetExport(library, "posix_spawn");
        byte* program = stackalloc byte[] { (byte)'/', (byte)'b', (byte)'i', (byte)'n', (byte)'/', (byte)'t', (byte)'r', (byte)'u', (byte)'e', 0 };
        byte** arguments = stackalloc byte*[2];
        arguments[0] = program;
        arguments[1] = null;
        byte** environment = stackalloc byte*[1];
        environment[0] = null;
        int child = 0;
        int result = spawn(&child, program, null, null, arguments, environment);
        if (result != 0) throw new IOException("The native Linux exit probe could not start /bin/true: " + result);
        return child;
    }
    finally { NativeLibrary.Free(library); }
}

static unsafe void ReapNativeChild(int child)
{
    nint library = NativeLibrary.Load("libc.so.6");
    try
    {
        var wait = (delegate* unmanaged[Cdecl]<int, int*, int, int>)NativeLibrary.GetExport(library, "waitpid");
        var error = (delegate* unmanaged[Cdecl]<int*>)NativeLibrary.GetExport(library, "__errno_location");
        int status = 0, result;
        do { result = wait(child, &status, 0); }
        while (result < 0 && *error() == 4); // EINTR leaves the child available for the required explicit reap.
        if (result != child || status != 0) throw new IOException("The native Linux exit probe did not reap its exact successful child.");
    }
    finally { NativeLibrary.Free(library); }
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
