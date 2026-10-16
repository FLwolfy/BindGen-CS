using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.Infrastructure.Workers;
using BGCS.Platforms.Targeting;
using Xunit;

namespace BGCS.Worker.Tests;

public sealed class CrashIsolationTests
{
    [Theory]
    [InlineData("exit", typeof(IOException))]
    [InlineData("partial", typeof(IOException))]
    [InlineData("oversized", typeof(InvalidDataException))]
    [InlineData("invalid-json", typeof(InvalidDataException))]
    [InlineData("wrong-id", typeof(InvalidDataException))]
    public async Task AbnormalWorkerResponse_FailsWithoutTerminatingHost(string mode, Type error)
    {
        int hostProcess = Environment.ProcessId;
        await Assert.ThrowsAsync(error, async () => await CreateClient().ParseAsync(Request(mode), CancellationToken.None));
        Assert.Equal(hostProcess, Environment.ProcessId);
    }

    [Fact]
    public async Task WorkerDiagnostics_AreDrainedWithBoundedRetention()
    {
        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await CreateClient().ParseAsync(Request("stderr-flood"), CancellationToken.None));
        Assert.Contains("LAST_DIAGNOSTIC", exception.Message, StringComparison.Ordinal);
        Assert.Contains("truncated", exception.Message, StringComparison.Ordinal);
        Assert.InRange(exception.Message.Length, 1, 66 * 1024);
    }

    [Fact]
    public async Task NativeRuntimeAbort_IsContainedInTheWorkerAndHostCanContinue()
    {
        int host = Environment.ProcessId;
        IOException failure = await Assert.ThrowsAsync<IOException>(async () =>
            await CreateClient().ParseAsync(Request("native-abort"), CancellationToken.None));
        Assert.Contains("NATIVE_ABORT_ENTERED", failure.Message, StringComparison.Ordinal);
        Assert.Equal(host, Environment.ProcessId);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await CreateClient().ParseAsync(Request("invalid-json"), CancellationToken.None));
    }

    [Fact]
    public async Task WrongParserIdentity_IsRejectedBeforeParsing()
    {
        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await CreateClient(parserIdentity: "different-parser").ParseAsync(Request("exit"), CancellationToken.None));
        Assert.Contains("parser identity", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Timeout_TerminatesTheWorkerProcess()
    {
        string pidFile = Path.Combine(Path.GetTempPath(), "bgcs-worker-pid-" + Guid.NewGuid().ToString("N"));
        try
        {
            await Assert.ThrowsAsync<TimeoutException>(async () =>
                await CreateClient(TimeSpan.FromSeconds(2)).ParseAsync(Request("hang", pidFile), CancellationToken.None));
            AssertProcessStopped(int.Parse(await File.ReadAllTextAsync(pidFile)));
        }
        finally { File.Delete(pidFile); File.Delete(pidFile + ".tmp"); }
    }

    [Theory]
    [InlineData("hang")]
    [InlineData("child")]
    public async Task Cancellation_TerminatesWorkerAndItsChildProcess(string mode)
    {
        string pidFile = Path.Combine(Path.GetTempPath(), "bgcs-worker-pid-" + Guid.NewGuid().ToString("N"));
        using CancellationTokenSource cancellation = new();
        Task? parse = null;
        try
        {
            parse = CreateClient().ParseAsync(Request(mode, pidFile), cancellation.Token).AsTask();
            using CancellationTokenSource startup = new(TimeSpan.FromSeconds(10));
            while (!File.Exists(pidFile))
                await Task.Delay(20, startup.Token);
            int processId = int.Parse(await File.ReadAllTextAsync(pidFile));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => parse);
            AssertProcessStopped(processId);
        }
        finally
        {
            cancellation.Cancel();
            if (parse is not null)
            {
                try { await parse.WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (OperationCanceledException) { }
            }
            File.Delete(pidFile);
            File.Delete(pidFile + ".tmp");
        }
    }

    private static void AssertProcessStopped(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            Assert.True(process.HasExited, $"Worker process {processId} survived cancellation.");
        }
        catch (ArgumentException) { /* The OS has already removed the terminated process. */ }
    }

    private static FrontendRequest Request(string mode, string? pidFile = null)
    {
        NativeTargetDescriptor target = new TargetResolver().Resolve(new(new NativeTargetId("host")));
        return new(target, SourceLanguage.C, pidFile is null ? [mode] : [mode, pidFile]);
    }

    private static FrontendWorkerClient CreateClient(TimeSpan? timeout = null, string parserIdentity = WorkerProtocol.C_PARSER_IDENTITY)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "BGCS.Frontend.Clang.Worker")))
            directory = directory.Parent;
        string root = directory?.FullName ?? throw new InvalidOperationException("The test repository is unavailable.");
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return new(Path.Combine(root, "tests", "fixtures", "hosts", "FaultingWorker", "bin", configuration, "net10.0", "FaultingWorker.dll"), timeout ?? TimeSpan.FromSeconds(15), parserIdentity: parserIdentity);
    }
}
