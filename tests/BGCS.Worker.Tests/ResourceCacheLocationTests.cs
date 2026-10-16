using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.Infrastructure.IO;
using BGCS.Infrastructure.Workers;
using BGCS.Platforms.Targeting;
using Xunit;

namespace BGCS.Worker.Tests;

public sealed class ResourceCacheLocationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LinuxWorkerKeepsBuiltinExtractionOutsideTheSourceDirectoryForFreshAndExistingProfiles(bool dataDirectoryExists)
    {
        if (!OperatingSystem.IsLinux()) return; // XDG_DATA_HOME is a Linux runtime contract.
        string root = Path.Combine(Path.GetTempPath(), "bgcs-worker-profile-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root, "source"), home = Path.Combine(root, "profile"), data = Path.Combine(home, "data");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(home);
        if (dataDirectoryExists) Directory.CreateDirectory(data);
        string? parentHome = Environment.GetEnvironmentVariable("HOME"), parentData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        try
        {
            string header = Path.Combine(source, "native.h");
            await File.WriteAllTextAsync(header, "#include <stddef.h>\nsize_t read_size(void);\n");
            var expectedMembership = DirectoryMembershipDigest.Capture(source);
            ProcessStartInfo start = new(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = source
            };
            start.ArgumentList.Add(WorkerPath());
            start.Environment["HOME"] = home;
            start.Environment["XDG_DATA_HOME"] = data;
            using Process worker = new() { StartInfo = start };
            Assert.True(worker.Start());
            using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(60));
            Task<string> errors = worker.StandardError.ReadToEndAsync(deadline.Token);
            try
            {
                WorkerHandshake handshake = await WorkerProtocol.ReadAsync<WorkerHandshake>(worker.StandardOutput.BaseStream, deadline.Token);
                Assert.Equal(WorkerProtocol.C_REVISION, handshake.protocolRevision);
                var target = new TargetResolver().Resolve(new(new NativeTargetId("host")));
                FrontendRequest request = new(target, SourceLanguage.C, [header]) { pathBase = source, parseMacros = false };
                await WorkerProtocol.WriteAsync(worker.StandardInput.BaseStream, new WorkerParseRequest("profile-cache", request), deadline.Token);
                worker.StandardInput.Close();
                WorkerResponse response = await WorkerProtocol.ReadAsync<WorkerResponse>(worker.StandardOutput.BaseStream, deadline.Token);
                await worker.WaitForExitAsync(deadline.Token);
                Assert.True(worker.ExitCode == 0 && response.error is null, response.error + Environment.NewLine + await errors);
                FrontendResult parsed = Assert.IsType<FrontendResult>(response.result);
                Assert.DoesNotContain(parsed.module.diagnostics, diagnostic => diagnostic.isFailure);
                Assert.Contains(parsed.module.declarations, declaration => declaration.name == "read_size" && declaration.kind == "Function");
                SourceSnapshot execution = Assert.IsType<SourceSnapshot>(parsed.executionInputs);
                Assert.Contains(execution.files, input => Path.GetFileName(input.path) == "stddef.h");
                string cache = Path.Combine(data, "BGCS", "ClangResources") + Path.DirectorySeparatorChar;
                Assert.All(execution.files, input => Assert.StartsWith(cache, input.path, StringComparison.Ordinal));
                Assert.True(SourceSnapshotVerifier.Matches(execution));
                Assert.True(SourceSnapshotVerifier.Matches(parsed.snapshot));
                Assert.Equal(expectedMembership, DirectoryMembershipDigest.Capture(source));
                Assert.False(Directory.Exists(Path.Combine(source, "BGCS")));
                Assert.Equal("native.h", Path.GetFileName(Assert.Single(Directory.GetFileSystemEntries(source))));
            }
            finally
            {
                if (!worker.HasExited) worker.Kill(entireProcessTree: true);
                await worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                await errors.WaitAsync(TimeSpan.FromSeconds(10));
            }
            Assert.Equal(parentHome, Environment.GetEnvironmentVariable("HOME"));
            Assert.Equal(parentData, Environment.GetEnvironmentVariable("XDG_DATA_HOME"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string WorkerPath()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "BindGen-CS.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(root.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0", "BGCS.Frontend.Clang.Worker.dll");
    }
}
