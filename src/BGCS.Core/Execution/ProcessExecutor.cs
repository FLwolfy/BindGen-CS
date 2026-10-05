using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace BGCS.Core.Execution;

/// <summary>
/// Owns shell-free tool execution, concurrent output draining, and termination before returning.
/// </summary>
public static class ProcessExecutor
{
    /// <summary>
    /// Starts a hidden noninteractive tool and waits until it exits, exceeds its deadline, or is canceled.
    /// </summary>
    /// <param name="executable">
    /// The executable path or name resolved by the operating system.
    /// </param>
    /// <param name="arguments">
    /// Individual arguments passed without shell interpolation.
    /// </param>
    /// <param name="workingDirectory">
    /// The directory in which the tool executes.
    /// </param>
    /// <param name="timeout">
    /// A positive deadline no greater than the system timer range.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels accepted work; cancellation is reported only after termination and output draining.
    /// </param>
    /// <returns>
    /// The exit status and captured output. Timeout returns a result with <c>timedOut</c> set;
    /// caller cancellation throws after process cleanup completes.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// The executable or working directory is empty.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// The argument collection is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The deadline is nonpositive or exceeds the system timer range.
    /// </exception>
    /// <exception cref="Win32Exception">
    /// The operating system cannot launch the requested executable.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Caller cancellation was requested.
    /// </exception>
    public static async Task<ProcessExecutionResult> ExecuteAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "The process deadline must fit a positive system timer interval.");
        cancellationToken.ThrowIfCancellationRequested();

        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException($"The operating system did not start '{executable}'.");
        process.StandardInput.Close();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(timeout);
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        bool timedOut = false;
        try
        {
            await process.WaitForExitAsync(wait.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            timedOut = deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
                // Exit may race with termination; output draining still belongs to this execution.
            }
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        await Task.WhenAll(output, error).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new ProcessExecutionResult(timedOut ? -1 : process.ExitCode, output.Result, error.Result, timedOut);
    }
}
