using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Execution;
using Xunit;

namespace BGCS.Core.Tests;

public sealed class ProcessExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_DrainsBothPipesAndClosesNoninteractiveInput()
    {
        string command = OperatingSystem.IsWindows()
            ? "$line = 'x' * 256; for ($index = 0; $index -lt 4096; $index++) { [Console]::Out.WriteLine($line); [Console]::Error.WriteLine($line) }; if ([Console]::In.ReadToEnd() -ne '') { exit 3 }; exit 7"
            : "i=0; while [ $i -lt 4096 ]; do printf '%0256d\\n' 0; printf '%0256d\\n' 0 >&2; i=$((i+1)); done; cat >/dev/null; exit 7";
        (string executable, string[] arguments) = Shell(command);

        ProcessExecutionResult result = await ProcessExecutor.ExecuteAsync(
            executable, arguments, Environment.CurrentDirectory, TimeSpan.FromSeconds(30));

        Assert.False(result.timedOut);
        Assert.Equal(7, result.exitCode);
        Assert.True(result.standardOutput.Length > 1_000_000);
        Assert.True(result.standardError.Length > 1_000_000);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_DeadlineAndCancellationTerminateAcceptedWork(bool cancel)
    {
        string directory = Path.Combine(Path.GetTempPath(), "bgcs-process-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string marker = Path.Combine(directory, "pid.txt");
        string quotedMarker = marker.Replace("'", "''", StringComparison.Ordinal);
        string command = OperatingSystem.IsWindows()
            ? $"[System.IO.File]::WriteAllText('{quotedMarker}', $PID.ToString()); [System.Threading.Thread]::Sleep(60000)"
            : $"printf '%s' $$ > '{quotedMarker}'; sleep 60";
        (string executable, string[] arguments) = Shell(command);
        using var cancellation = new CancellationTokenSource();
        try
        {
            Task<ProcessExecutionResult> execution = ProcessExecutor.ExecuteAsync(
                executable, arguments, directory, cancel ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(10), cancellation.Token);
            if (cancel)
            {
                Stopwatch wait = Stopwatch.StartNew();
                while (!File.Exists(marker) && wait.Elapsed < TimeSpan.FromSeconds(10))
                    await Task.Delay(20);
                Assert.True(File.Exists(marker));
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
            }
            else
            {
                ProcessExecutionResult result = await execution;
                Assert.True(result.timedOut);
                Assert.Equal(-1, result.exitCode);
            }
            Assert.True(File.Exists(marker));
            int processId = int.Parse(File.ReadAllText(marker));
            Assert.False(IsRunning(processId));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static bool IsRunning(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static (string executable, string[] arguments) Shell(string command)
        => OperatingSystem.IsWindows()
            ? (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
                ["-NoProfile", "-NonInteractive", "-Command", command])
            : ("/bin/sh", ["-c", command]);
}
