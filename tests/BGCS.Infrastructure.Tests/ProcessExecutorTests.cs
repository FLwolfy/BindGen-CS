using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Infrastructure.Execution;
using Xunit;

namespace BGCS.Infrastructure.Tests;

public sealed class ProcessExecutorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_DecodesRawChildStreamsWithExplicitEncodingOrExistingDefault(bool unicode)
    {
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "BindGen-CS.slnx"))) repository = repository.Parent;
        Assert.NotNull(repository);
        string configuration = typeof(ProcessExecutorTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        string fixture = Path.Combine(repository.FullName, "tests", "fixtures", "hosts", "ProcessProbe", "bin", configuration, "net10.0", "ProcessProbe.dll");
        string output = unicode ? "验收🙂 café" : "unchanged ASCII stdout";
        string error = unicode ? "错误λ stderr" : "unchanged ASCII stderr";
        ProcessExecutionResult result = await ProcessExecutor.ExecuteAsync(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            [fixture, "raw-utf8", output, error], repository.FullName, TimeSpan.FromSeconds(20), outputEncoding: unicode ? Encoding.UTF8 : null);
        Assert.Equal(7, result.exitCode);
        Assert.False(result.timedOut);
        Assert.Equal(output, result.standardOutput);
        Assert.Equal(error, result.standardError);
    }

    [Fact]
    public async Task ExecuteAsync_ChildEnvironmentCanOverrideAndRemoveValuesWithoutChangingTheParent()
    {
        string variable = "BGCS_PROCESS_ENVIRONMENT_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(variable, "parent-value");
        string command = OperatingSystem.IsWindows() ? "[Console]::Out.Write([Environment]::GetEnvironmentVariable('" + variable + "'))" : "printf '%s' \"$" + variable + "\"";
        (string executable, string[] arguments) = Shell(command);
        try
        {
            ProcessExecutionResult replaced = await ProcessExecutor.ExecuteAsync(executable, arguments, Environment.CurrentDirectory, TimeSpan.FromSeconds(15),
                environmentOverrides: new Dictionary<string, string?> { [variable] = "child-value & 50%" });
            Assert.Equal("child-value & 50%", replaced.standardOutput);
            Assert.Equal("parent-value", Environment.GetEnvironmentVariable(variable));
            ProcessExecutionResult removed = await ProcessExecutor.ExecuteAsync(executable, arguments, Environment.CurrentDirectory, TimeSpan.FromSeconds(15),
                environmentOverrides: new Dictionary<string, string?> { [variable] = null });
            Assert.Empty(removed.standardOutput);
            Assert.Equal("parent-value", Environment.GetEnvironmentVariable(variable));
        }
        finally { Environment.SetEnvironmentVariable(variable, null); }
    }

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
    public async Task ExecuteAsync_RejectsOversizedOutputAfterDrainingBothStreams(bool standardError)
    {
        string command = OperatingSystem.IsWindows()
            ? "[Console]::" + (standardError ? "Error" : "Out") + ".Write('x' * 262144)"
            : "head -c 262144 /dev/zero" + (standardError ? " >&2" : string.Empty);
        (string executable, string[] arguments) = Shell(command);
        IOException failure = await Assert.ThrowsAsync<IOException>(() => ProcessExecutor.ExecuteAsync(
            executable, arguments, Environment.CurrentDirectory, TimeSpan.FromSeconds(30), maximumCapturedCharacters: 32768));
        Assert.Contains("capture limit", failure.Message);
    }

    [Fact]
    public async Task ExecuteAsync_CaptureLimitIsInclusive()
    {
        string command = OperatingSystem.IsWindows() ? "[Console]::Out.Write('x' * 8192)" : "head -c 8192 /dev/zero";
        (string executable, string[] arguments) = Shell(command);
        ProcessExecutionResult result = await ProcessExecutor.ExecuteAsync(
            executable, arguments, Environment.CurrentDirectory, TimeSpan.FromSeconds(30), maximumCapturedCharacters: 8192);
        Assert.Equal(8192, result.standardOutput.Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_DeadlineAndCancellationTerminateAcceptedWork(bool cancel)
    {
        string directory = Path.Combine(Path.GetTempPath(), "bgcs-process-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string marker = Path.Combine(directory, "pid.txt");
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "BindGen-CS.slnx"))) repository = repository.Parent;
        Assert.NotNull(repository);
        string configuration = typeof(ProcessExecutorTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        string fixture = Path.Combine(repository.FullName, "tests", "fixtures", "hosts", "ProcessProbe", "bin", configuration, "net10.0", "ProcessProbe.dll");
        using var cancellation = new CancellationTokenSource();
        Task<ProcessExecutionResult> execution = ProcessExecutor.ExecuteAsync(
            Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", [fixture, "wait-ready", marker],
            directory, cancel ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(10), cancellation.Token);
        int? acceptedProcessId = null;
        try
        {
            if (cancel)
            {
                // Cancellation exercises work whose readiness is acknowledged,
                // rather than assuming shell/runtime startup fits ten seconds.
                while (!File.Exists(marker))
                {
                    Assert.False(execution.IsCompleted, "The child exited before acknowledging readiness.");
                    await Task.Delay(20);
                }
                acceptedProcessId = int.Parse(File.ReadAllText(marker), CultureInfo.InvariantCulture);
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
            }
            else
            {
                ProcessExecutionResult result = await execution;
                Assert.True(result.timedOut);
                Assert.Equal(-1, result.exitCode);
            }
            // A process deadline includes startup and can expire before entry.
            // Whenever the child reached entry, its real PID must be gone.
            if (acceptedProcessId is null && File.Exists(marker))
                acceptedProcessId = int.Parse(File.ReadAllText(marker), CultureInfo.InvariantCulture);
            if (acceptedProcessId is int processId) Assert.False(IsRunning(processId));
        }
        finally
        {
            cancellation.Cancel();
            try { await execution; }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ExitedParentWithInheritedPipeHasABoundedCleanupDeadline()
    {
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "BindGen-CS.slnx"))) repository = repository.Parent;
        Assert.NotNull(repository);
        string configuration = typeof(ProcessExecutorTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        string fixture = Path.Combine(repository.FullName, "tests", "fixtures", "hosts", "ProcessProbe", "bin", configuration, "net10.0", "ProcessProbe.dll");
        string marker = Path.Combine(Path.GetTempPath(), "bgcs-inherited-pipe-" + Guid.NewGuid().ToString("N"));
        Stopwatch elapsed = Stopwatch.StartNew();
        try
        {
            IOException failure = await Assert.ThrowsAsync<IOException>(() => ProcessExecutor.ExecuteAsync(
                Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", [fixture, "parent", marker], repository.FullName, TimeSpan.FromSeconds(20)));
            Assert.Contains("inherited output pipe", failure.Message);
            Assert.InRange(elapsed.Elapsed, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
        }
        finally
        {
            if (File.Exists(marker))
            {
                int childId = int.Parse(File.ReadAllText(marker));
                if (IsRunning(childId))
                {
                    using Process child = Process.GetProcessById(childId);
                    child.Kill(entireProcessTree: true);
                    await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
                File.Delete(marker);
            }
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
