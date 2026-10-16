using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BGCS.Infrastructure.Execution;

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
    /// <param name="maximumCapturedCharacters">
    /// The positive per-stream capture limit; larger output is drained and then rejected rather than silently truncated.
    /// </param>
    /// <param name="environmentOverrides">Explicit child-only environment changes; null values remove inherited variables.</param>
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
    /// <exception cref="IOException">
    /// A terminated process did not exit, an inherited pipe did not close, or output exceeded its capture limit.
    /// </exception>
    public static async Task<ProcessExecutionResult> ExecuteAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken = default,
        int maximumCapturedCharacters = 16 * 1024 * 1024,
        IReadOnlyDictionary<string, string?>? environmentOverrides = null
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "The process deadline must fit a positive system timer interval.");
        if (maximumCapturedCharacters <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumCapturedCharacters));
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
        if (environmentOverrides is not null)
            foreach ((string name, string? value) in environmentOverrides)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(name);
                if (name.Contains('=') || name.Contains('\0') || value?.Contains('\0') == true)
                    throw new ArgumentException("A child process environment override has an invalid name or value.", nameof(environmentOverrides));
                if (value is null) start.Environment.Remove(name);
                else start.Environment[name] = value;
            }
        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException($"The operating system did not start '{executable}'.");
        process.StandardInput.Close();
        using CancellationTokenSource drain = new();
        Task<CapturedOutput> output = ReadBoundedAsync(process.StandardOutput, maximumCapturedCharacters, drain.Token);
        Task<CapturedOutput> error = ReadBoundedAsync(process.StandardError, maximumCapturedCharacters, drain.Token);
        using var deadline = new CancellationTokenSource(timeout);
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        bool timedOut = false;
        try
        {
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
                    // Exit may race with termination; pipe ownership remains with this execution.
                }
                try
                {
                    await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
                catch (TimeoutException exception)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new IOException("The terminated tool did not exit within the cleanup deadline.", exception);
                }
            }
            try
            {
                await Task.WhenAll(output, error).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (TimeoutException exception)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new IOException("The tool exited but an inherited output pipe remained open after the cleanup deadline.", exception);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (output.Result.truncated || error.Result.truncated)
                throw new IOException($"Tool output exceeded the {maximumCapturedCharacters}-character per-stream capture limit; incomplete output cannot be used for validation.");
            return new ProcessExecutionResult(timedOut ? -1 : process.ExitCode, output.Result.text, error.Result.text, timedOut);
        }
        finally
        {
            await drain.CancelAsync().ConfigureAwait(false);
            process.StandardOutput.Close();
            process.StandardError.Close();
            // Closing inherited pipes cancels the reads without allowing orphaned read failures to go unobserved.
            Observe(output);
            Observe(error);
        }
    }

    private sealed record CapturedOutput(
        string text,
        bool truncated
    );

    private static async Task<CapturedOutput> ReadBoundedAsync(
        StreamReader reader,
        int limit,
        CancellationToken cancellationToken
    ) {
        StringBuilder captured = new(Math.Min(limit, 4096));
        char[] buffer = new char[8192];
        bool truncated = false;
        while (true)
        {
            int count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (count == 0) return new(captured.ToString(), truncated);
            int retained = Math.Min(count, limit - captured.Length);
            captured.Append(buffer, 0, retained);
            truncated |= retained != count;
        }
    }

    private static void Observe(Task task)
        => _ = task.ContinueWith(completed => _ = completed.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}
