using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using BGCS.Contracts.Requests;
using BGCS.NativeBuild.Execution;
using BGCS.Platforms.Targeting;
using Xunit;

namespace BGCS.NativeBuild.Tests;

public sealed class NativeOperationFailureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealToolDeadlineAndNonzeroExitRetainTheirStatusAndStopAllLaterOperations(bool timeOut)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-native-operation-failure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string entered = Path.Combine(root, "entered.pid"), later = Path.Combine(root, "must-not-run");
        string output = Path.Combine(root, "must-not-exist.bin");
        try
        {
            DirectoryInfo? repository = new(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "BindGen-CS.slnx"))) repository = repository.Parent;
            Assert.NotNull(repository);
            string configuration = typeof(NativeOperationFailureTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
            string fixture = Path.Combine(repository.FullName, "tests", "fixtures", "hosts", "ProcessProbe", "bin", configuration, "net10.0", "ProcessProbe.dll");
            Assert.True(File.Exists(fixture), fixture);
            string executable = NativeBuildToolDiscovery.FindExecutable("dotnet", Environment.GetEnvironmentVariable("DOTNET_HOST_PATH"))
                ?? throw new InvalidOperationException("The process control requires its actual .NET host executable.");
            System.Collections.Immutable.ImmutableArray<string> first = timeOut
                ? [fixture, "wait-ready", entered]
                : [fixture, "raw-utf8", "native-stdout-sentinel", "native-stderr-sentinel"];
            NativeBuildPlan plan = new("failure-probe", new TargetResolver().Resolve(new(new("host"))),
                [new("objects", executable, first, root), new("must-not-run", executable, [fixture, "wait-ready", later], root)], output)
            { scratchDirectory = root, externalInputFiles = [fixture] };
            Exception failure = timeOut
                ? await Assert.ThrowsAsync<TimeoutException>(() => new PlannedNativeBuildExecutor().ExecuteAsync(plan, TimeSpan.FromSeconds(10)))
                : await Assert.ThrowsAsync<InvalidOperationException>(() => new PlannedNativeBuildExecutor().ExecuteAsync(plan, TimeSpan.FromSeconds(10)));
            Assert.Contains("Native operation 'objects' failed:", failure.Message);
            Assert.Contains("timedOut=" + timeOut, failure.Message);
            Assert.Contains("exitCode=" + (timeOut ? "-1" : "7"), failure.Message);
            Assert.Contains("executable='" + executable + "'", failure.Message);
            Assert.Contains("shared deadline=10.000s", failure.Message);
            Assert.Contains("launch allowance=", failure.Message);
            Assert.Contains("elapsed=", failure.Message);
            if (!timeOut)
            {
                Assert.Contains("native-stdout-sentinel", failure.Message);
                Assert.Contains("native-stderr-sentinel", failure.Message);
            }
            // The shared deadline includes runtime startup. If it expires
            // before application entry, no readiness file or output is owed.
            // When entry was acknowledged atomically, that child must be gone.
            if (File.Exists(entered))
            {
                int processId = int.Parse(File.ReadAllText(entered), System.Globalization.CultureInfo.InvariantCulture);
                Assert.False(IsRunning(processId));
            }
            Assert.False(File.Exists(later));
            Assert.False(File.Exists(output));
        }
        finally { Directory.Delete(root, recursive: true); }

    }

    private static bool IsRunning(int processId)
    {
        try { using Process process = Process.GetProcessById(processId); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }
}
