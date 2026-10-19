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
        Assert.Contains("fixture failure summary", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("LAST_DIAGNOSTIC", exception.Message, StringComparison.Ordinal);
        string details = Assert.IsType<IOException>(exception.InnerException).Message;
        Assert.Contains("LAST_DIAGNOSTIC", details, StringComparison.Ordinal);
        Assert.Contains("truncated", details, StringComparison.Ordinal);
        Assert.InRange(exception.Message.Length, 1, 2300);
        Assert.InRange(details.Length, 1, 66 * 1024);
    }

    [Fact]
    public async Task NativeRuntimeAbort_IsContainedInTheWorkerAndHostCanContinue()
    {
        int host = Environment.ProcessId;
        IOException failure = await Assert.ThrowsAsync<IOException>(async () =>
            await CreateClient().ParseAsync(Request("native-abort"), CancellationToken.None));
        Assert.Contains("request", failure.Message, StringComparison.Ordinal);
        Assert.Contains("NATIVE_ABORT_ENTERED", Assert.IsType<IOException>(failure.InnerException).Message, StringComparison.Ordinal);
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
            await AssertProcessStoppedAsync(int.Parse(await File.ReadAllTextAsync(pidFile)));
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
            int workerId = mode == "child" ? int.Parse(await File.ReadAllTextAsync(pidFile + ".worker")) : processId;
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => parse);
            await AssertProcessStoppedAsync(workerId);
            await AssertProcessStoppedAsync(processId);
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
            File.Delete(pidFile + ".worker");
            File.Delete(pidFile + ".worker.tmp");
        }
    }

    [Fact]
    public async Task TerminationProbeRejectsALiveProcessAndRecognizesActualKernelTermination()
    {
        Assert.False(HasProcessStopped(Environment.ProcessId, out string live), live);
        string marker = Path.Combine(Path.GetTempPath(), "bgcs-exit-probe-" + Guid.NewGuid().ToString("N"));
        ProcessStartInfo start = new(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true
        };
        start.ArgumentList.Add(WorkerPath());
        start.ArgumentList.Add(OperatingSystem.IsLinux() ? "--zombie-probe" : "--exit-probe");
        start.ArgumentList.Add(marker);
        using Process parent = Process.Start(start)!;
        try
        {
            using CancellationTokenSource startup = new(TimeSpan.FromSeconds(10));
            if (OperatingSystem.IsLinux())
            {
                while (!File.Exists(marker)) await Task.Delay(20, startup.Token);
                int zombieId = int.Parse(await File.ReadAllTextAsync(marker, startup.Token));
                string state;
                while (!HasProcessStopped(zombieId, out state)) await Task.Delay(20, startup.Token);
                Assert.Contains("state=Z", state, StringComparison.Ordinal);
                using Process zombie = Process.GetProcessById(zombieId);
                Assert.False(zombie.HasExited); // Non-child HasExited uses kill(pid, 0), which also succeeds for zombies.
                Assert.False(parent.HasExited); // Its native parent has deliberately not called waitpid yet.
                await AssertProcessStoppedAsync(zombieId);
            }
            else
            {
                await parent.WaitForExitAsync(startup.Token);
                Assert.True(HasProcessStopped(parent.Id, out string stopped), stopped);
            }
        }
        finally
        {
            if (!parent.HasExited)
            {
                // The Linux probe explicitly reaps its native child before exiting.
                await parent.StandardInput.WriteLineAsync("reap");
                parent.StandardInput.Close();
                try { await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
                finally { if (!parent.HasExited) parent.Kill(entireProcessTree: true); }
            }
            File.Delete(marker);
            File.Delete(marker + ".tmp");
        }
        Assert.Equal(0, parent.ExitCode);
    }

    [Theory]
    [InlineData('R', false)]
    [InlineData('S', false)]
    [InlineData('D', false)]
    [InlineData('T', false)]
    [InlineData('t', false)]
    [InlineData('Z', true)]
    [InlineData('X', true)]
    [InlineData('x', true)]
    public void LinuxStateProbePreservesEveryLiveStateAndParsesParenthesesInTheProcessName(char state, bool stopped)
        => Assert.Equal(stopped, IsTerminatedLinuxState("42 (name ) with (brackets)) " + state + " 1 2 3", 42, out _));

    private static async Task AssertProcessStoppedAsync(int processId)
    {
        Stopwatch wait = Stopwatch.StartNew();
        string observation;
        while (!HasProcessStopped(processId, out observation) && wait.Elapsed < TimeSpan.FromSeconds(5))
            await Task.Delay(20);
        Assert.True(HasProcessStopped(processId, out observation), $"Worker process {processId} survived cancellation: {observation}");
    }

    private static bool HasProcessStopped(int processId, out string observation)
    {
        if (OperatingSystem.IsLinux())
        {
            try
            {
                // A zombie has already stopped executing and released its descriptors;
                // only its parent's exit-status bookkeeping remains in /proc.
                return IsTerminatedLinuxState(File.ReadAllText($"/proc/{processId}/stat"), processId, out observation);
            }
            catch (FileNotFoundException) { observation = "PID absent from /proc"; return true; }
            catch (DirectoryNotFoundException) { observation = "PID absent from /proc"; return true; }
            catch (IOException) when (!Directory.Exists($"/proc/{processId}")) { observation = "PID removed during /proc read"; return true; }
        }
        try
        {
            using Process process = Process.GetProcessById(processId);
            bool exited = process.HasExited;
            observation = "HasExited=" + exited;
            return exited;
        }
        catch (ArgumentException) { observation = "PID absent from process table"; return true; }
    }

    private static bool IsTerminatedLinuxState(string stat, int processId, out string observation)
    {
        int opening = stat.IndexOf('('), closing = stat.LastIndexOf(')');
        if (opening < 1 || closing <= opening || closing + 3 >= stat.Length || stat[closing + 1] != ' ' || stat[closing + 3] != ' '
            || !int.TryParse(stat.AsSpan(0, opening).Trim(), out int actualId) || actualId != processId)
            throw new InvalidDataException("The Linux process state has no valid PID/name/state record.");
        char state = stat[closing + 2];
        observation = "Linux state=" + state + "; " + stat[..(closing + 1)];
        return state is 'Z' or 'X' or 'x';
    }

    private static FrontendRequest Request(string mode, string? pidFile = null)
    {
        NativeTargetDescriptor target = new TargetResolver().Resolve(new(new NativeTargetId("host")));
        return new(target, SourceLanguage.C, pidFile is null ? [mode] : [mode, pidFile]);
    }

    private static FrontendWorkerClient CreateClient(TimeSpan? timeout = null, string parserIdentity = WorkerProtocol.C_PARSER_IDENTITY)
        => new(WorkerPath(), timeout ?? TimeSpan.FromSeconds(15), parserIdentity: parserIdentity);

    private static string WorkerPath()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "BGCS.Frontend.Clang.Worker")))
            directory = directory.Parent;
        string root = directory?.FullName ?? throw new InvalidOperationException("The test repository is unavailable.");
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(root, "tests", "fixtures", "hosts", "FaultingWorker", "bin", configuration, "net10.0", "FaultingWorker.dll");
    }
}
