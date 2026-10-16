using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;
using BGCS.Infrastructure.Workers;
using Xunit;
using Xunit.Abstractions;

namespace BGCS.Tests;

public sealed class SourceContributionCleanupTests(ITestOutputHelper output)
{
    [Fact]
    public async Task WindowsOwnedStoreCleanupWarningPreservesCommitAndServiceFailureKeepsBothErrorsUntilRetry()
    {
        if (!OperatingSystem.IsWindows())
        {
            output.WriteLine("Platform-gated: this fault injection requires Windows ReadOnly file deletion semantics; it was not executed on this host.");
            return;
        }
        string root = Path.Combine(Path.GetTempPath(), "bgcs-source-cleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        StoreState state = new();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "original.h"), "#pragma once\nint original(void);\n");
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "source-cleanup-" + Guid.NewGuid().ToString("N"), nativeName = "source_cleanup" },
                source = new() { language = SourceLanguage.C, headers = ["original.h"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
                api = new() { @namespace = "Generated.SourceCleanup", name = "CleanupApi" },
                runtime = new() { mode = "source" }, cache = new() { enabled = false }
            };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns()
                .WithFrontendFactory(() => new ProtectOwnedFileFrontend(WorkerRoot(), state))
                .AddSourceContributor("cleanup.store", 0, () => new Contributor(state)).Build();
            GenerationRequest request = new(configuration, root);
            GenerationResult first = await host.GenerateAsync(request);
            Assert.True(first.published);
            Assert.True(first.success, first.GetDiagnosticSummary());
            Assert.DoesNotContain(first.diagnostics, diagnostic => diagnostic.isFailure);
            BindingDiagnostic maintenance = Assert.Single(first.diagnostics, diagnostic => diagnostic.code == "BGCS-SOURCE-CLEANUP");
            Assert.Equal(BindingDiagnosticSeverity.Warning, maintenance.severity);
            string captured = Assert.IsType<string>(state.file);
            string store = StoreRoot(captured);
            Assert.Contains(captured, maintenance.message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(captured));
            Assert.True((File.GetAttributes(captured) & FileAttributes.ReadOnly) != 0);
            Assert.Equal(1, state.serviceReleases);
            AssertLockReleased(store);
            Dictionary<string, byte[]> previous = Snapshot(first.outputRoot);
            Assert.NotEmpty(previous);
            foreach (var artifact in first.artifacts)
                Assert.True(File.Exists(Path.Combine(first.outputRoot, artifact.relativePath)), artifact.relativePath);

            // The same immutable store still exists. Service cleanup now fails
            // before parsing, and deleting that retained store also fails.
            state.failServiceRelease = true;
            GenerationResult rejected = await host.GenerateAsync(request);
            Assert.False(rejected.success);
            Assert.False(rejected.published);
            Assert.Empty(rejected.artifacts);
            Assert.Contains(rejected.diagnostics, diagnostic => diagnostic.isFailure);
            Assert.Contains("contributor-service-cleanup-sentinel", rejected.GetDiagnosticSummary(), StringComparison.Ordinal);
            Assert.Contains(captured, rejected.GetDiagnosticSummary(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(2, state.serviceReleases);
            Assert.Equal(1, state.parses);
            AssertSame(previous, Snapshot(first.outputRoot));
            AssertLockReleased(store);

            File.SetAttributes(captured, state.originalAttributes);
            state.failServiceRelease = false;
            using CancellationTokenSource deadline = new(TimeSpan.FromMinutes(2));
            GenerationResult retry = await host.GenerateAsync(request, deadline.Token);
            Assert.True(retry.success, retry.GetDiagnosticSummary());
            Assert.True(retry.published);
            Assert.DoesNotContain(retry.diagnostics, diagnostic => diagnostic.code == "BGCS-SOURCE-CLEANUP" || diagnostic.isFailure);
            Assert.Equal(3, state.serviceReleases);
            Assert.Equal(2, state.parses);
            Assert.False(Directory.Exists(store));
            Assert.False(File.Exists(store + ".lock"));
        }
        finally
        {
            // Touch only the exact store captured from this test's public
            // frontend request. Never enumerate or recursively clear Temp stores.
            if (state.file is { } file)
            {
                string store = StoreRoot(file);
                if (File.Exists(file))
                {
                    File.SetAttributes(file, state.originalAttributes);
                    File.Delete(file);
                }
                for (string? directory = Path.GetDirectoryName(file); directory is not null; directory = Path.GetDirectoryName(directory))
                {
                    if (Directory.Exists(directory)) Directory.Delete(directory, recursive: false);
                    if (directory.Equals(store, StringComparison.OrdinalIgnoreCase)) break;
                }
            }
            Directory.Delete(root, recursive: true);
        }
    }

    private static void AssertLockReleased(string store)
    {
        using FileStream lease = new(store + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        Assert.True(lease.CanWrite);
    }

    private static string StoreRoot(string file)
    {
        string suffix = Path.Combine("native", "include", "contributions", "cleanup.store", "generated.h");
        string full = Path.GetFullPath(file);
        Assert.EndsWith(Path.DirectorySeparatorChar + suffix, full, StringComparison.OrdinalIgnoreCase);
        string store = full[..^suffix.Length].TrimEnd(Path.DirectorySeparatorChar);
        Assert.Equal(64, Path.GetFileName(store).Length);
        Assert.All(Path.GetFileName(store), character => Assert.True(char.IsAsciiHexDigit(character)));
        Assert.Equal(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "BGCS", "source-inputs", "v1")),
            Path.GetDirectoryName(store), ignoreCase: true);
        return store;
    }

    private static Dictionary<string, byte[]> Snapshot(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(root, path), File.ReadAllBytes, StringComparer.Ordinal);
    private static void AssertSame(Dictionary<string, byte[]> expected, Dictionary<string, byte[]> actual)
    {
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), actual.Keys.Order(StringComparer.Ordinal));
        foreach ((string path, byte[] bytes) in expected) Assert.Equal(bytes, actual[path]);
    }

    private sealed class StoreState
    {
        internal string? file;
        internal FileAttributes originalAttributes;
        internal bool protect = true;
        internal bool failServiceRelease;
        internal int serviceReleases;
        internal int parses;
    }

    private sealed class Contributor(StoreState state) : ISourceContributor, IDisposable
    {
        public string id => "cleanup.store";
        public int priority => 0;
        public ValueTask<SourceContribution> ContributeAsync(SourceContributionRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new SourceContribution([SourceContributionArtifact.FromUtf8("generated.h", SourceContributionRole.PublicHeader,
                "text/x-c-header", "#pragma once\nint contributed(void);\n", SourceLanguage.C, exposeToBindings: true)], new(BGCS.Contracts.Common.EvidenceLevel.Declared, "cleanup fixture")));
        }
        public void Dispose()
        {
            state.serviceReleases++;
            if (state.failServiceRelease) throw new InvalidOperationException("contributor-service-cleanup-sentinel");
        }
    }

    private sealed class ProtectOwnedFileFrontend(string workerRoot, StoreState state) : IFrontend
    {
        private readonly FrontendWorkerClient m_worker = new(Path.Combine(workerRoot, "BGCS.Frontend.Clang.Worker.dll"));
        public async ValueTask<FrontendResult> ParseAsync(FrontendRequest request, CancellationToken cancellationToken)
        {
            FrontendResult result = await m_worker.ParseAsync(request, cancellationToken);
            state.parses++;
            string file = request.sourcePathMappings.Single(pair => pair.Value == "contributions/cleanup.store/generated.h").Key;
            _ = StoreRoot(file);
            if (state.protect)
            {
                state.file = file;
                state.originalAttributes = File.GetAttributes(file);
                File.SetAttributes(file, state.originalAttributes | FileAttributes.ReadOnly);
                state.protect = false;
            }
            else Assert.Equal(state.file, file);
            return result;
        }
    }

    private static string WorkerRoot()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "BindGen-CS.slnx"))) root = root.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(root!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
