using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace BGCS.Tool.Tests;

internal static class CliInvocation {
    internal static int Run(
        string[] args,
        string directory,
        TextWriter output,
        TextWriter error
    ) {
        string host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(typeof(object).Assembly.Location)!, "..", "..", "..",
            OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
        ProcessStartInfo start = new(host) {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "BGCS.Tool.dll"));
        foreach (string argument in args)
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("The CLI process could not start.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000)) {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("The CLI exceeded its test deadline.");
        }
        Task.WaitAll(stdout, stderr);
        output.Write(stdout.Result);
        error.Write(stderr.Result);
        return process.ExitCode;
    }
}
