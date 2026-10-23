using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Core.Ports;
using BGCS.Core.Requests;
using Xunit;

namespace BGCS.Tests;

public sealed class WorkspaceExecutionTests
{
    [Fact]
    public async Task Diff_UsesTheSamePrerequisiteGraphWithoutPublishingAnyProject()
    {
        using Scratch scratch = new();
        ConcurrentQueue<string> parsed = new();
        await using BindingHost host = BindingGenerator.CreateBuilder().UseDetachedFrontend(() => new ProbeFrontend(parsed, fail: "bad")).Build();
        BindingWorkspace workspace = new([Project(scratch.root, "dependent", ["bad"]), Project(scratch.root, "independent", []), Project(scratch.root, "bad", [])], TestHostPaths.Create());
        WorkspaceDiffResult result = await host.DiffWorkspaceAsync(workspace);
        Assert.False(result.success);
        Assert.True(result.hasChanges);
        Assert.Equal(WorkspaceProjectStatus.Skipped, result.projects[0].status);
        Assert.True(result.projects[1].diff!.hasChanges);
        Assert.Equal(WorkspaceProjectStatus.Failed, result.projects[2].status);
        Assert.DoesNotContain("dependent", parsed);
        Assert.False(Directory.Exists(Path.Combine(scratch.root, "out")));
    }

    [Fact]
    public void Graph_RejectsCyclesDuplicatesAndUnknownDependenciesBeforeCreatingOutputs()
    {
        using Scratch scratch = new();
        WorkspaceProject first = Project(scratch.root, "first", ["second"]);
        WorkspaceProject second = Project(scratch.root, "second", ["first"]);
        Assert.Contains("cycle", Assert.Throws<ArgumentException>(() => new BindingWorkspace([first, second], TestHostPaths.Create())).Message);
        Assert.Contains("duplicated", Assert.Throws<ArgumentException>(() => new BindingWorkspace([first, first], TestHostPaths.Create())).Message);
        Assert.Contains("unknown", Assert.Throws<ArgumentException>(() => new BindingWorkspace([first], TestHostPaths.Create())).Message);
        Assert.False(Directory.Exists(Path.Combine(scratch.root, "out")));
    }

    [Fact]
    public void Graph_RejectsNestedOutputsAndCrossProjectSourceOrCacheOverlap()
    {
        using Scratch scratch = new();
        WorkspaceProject first = Project(scratch.root, "first", []);
        WorkspaceProject second = Project(scratch.root, "second", []);
        GenerationRequest nested = second.request.WithConfiguration(second.request.configuration with { output = new() { root = "out/first/nested" } });
        Assert.Contains("overlap", Assert.Throws<ArgumentException>(() => new BindingWorkspace([first, second with { request = nested }], TestHostPaths.Create())).Message);
        GenerationRequest source = second.request.WithConfiguration(second.request.configuration with { source = new() { headers = ["out/first/native.h"] } });
        Assert.Contains("protected input", Assert.Throws<ArgumentException>(() => new BindingWorkspace([first, second with { request = source }], TestHostPaths.Create())).Message);
        GenerationRequest cache = second.request.WithConfiguration(second.request.configuration with { cache = new() { directory = "out/first/cache" } });
        Assert.Contains("cache", Assert.Throws<ArgumentException>(() => new BindingWorkspace([first, second with { request = cache }], TestHostPaths.Create())).Message);
    }

    [Fact]
    public void Loader_UsesEachConfigurationBaseAndRejectsAmbiguousFieldsAndTargets()
    {
        using Scratch scratch = new();
        string projectRoot = Path.Combine(scratch.root, "nested");
        Directory.CreateDirectory(projectRoot);
        File.WriteAllText(Path.Combine(projectRoot, "bindgen.json"), """
            {"schemaVersion":2,"library":{"id":"first","nativeName":"first"},"source":{"headers":["first.h"]},
             "targets":[{"id":"host"},{"id":"fixture-secondary"}],"output":{"root":"generated"}}
            """);
        string workspacePath = Path.Combine(scratch.root, "workspace.json");
        string json = """
            {"schemaVersion":1,"projects":[{"id":"first","configuration":"nested/bindgen.json","targets":["fixture-secondary"]}]}
            """;
        File.WriteAllText(workspacePath, json);
        BindingWorkspace workspace = new WorkspaceLoader(TestHostPaths.Create()).Load(workspacePath);
        WorkspaceProject project = Assert.Single(workspace.projects);
        Assert.Equal(projectRoot, project.request.baseDirectory);
        Assert.Equal("fixture-secondary", Assert.Single(project.request.configuration.targets).id);
        Assert.Contains(project.request.configurationSnapshot.files, input => input.path == workspacePath);
        File.WriteAllText(workspacePath, json.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1", StringComparison.Ordinal));
        Assert.Throws<JsonException>(() => new WorkspaceLoader(TestHostPaths.Create()).Load(workspacePath));
        File.WriteAllText(workspacePath, json.Replace("fixture-secondary", "unknown-target", StringComparison.Ordinal));
        Assert.Throws<JsonException>(() => new WorkspaceLoader(TestHostPaths.Create()).Load(workspacePath));
        File.WriteAllText(workspacePath, json.Replace("\"projects\":", "\"unknown\":true,\"projects\":", StringComparison.Ordinal));
        Assert.Throws<JsonException>(() => new WorkspaceLoader(TestHostPaths.Create()).Load(workspacePath));
    }

    [Fact]
    public async Task Executor_FailsDependentsButCompletesIndependentBranchesInPresentationOrder()
    {
        using Scratch scratch = new();
        ConcurrentQueue<string> parsed = new();
        await using BindingHost host = BindingGenerator.CreateBuilder().UseDetachedFrontend(() => new ProbeFrontend(parsed, fail: "bad")).Build();
        BindingWorkspace workspace = new([Project(scratch.root, "dependent", ["bad"]), Project(scratch.root, "independent", []), Project(scratch.root, "bad", [])], TestHostPaths.Create());
        WorkspaceResult result = await host.GenerateWorkspaceAsync(workspace);
        Assert.False(result.success);
        Assert.Equal(["dependent", "independent", "bad"], result.projects.Select(project => project.projectId).ToArray());
        Assert.Equal(WorkspaceProjectStatus.Skipped, result.projects[0].status);
        Assert.Null(result.projects[0].generation);
        Assert.Equal(WorkspaceProjectStatus.Completed, result.projects[1].status);
        Assert.Equal(WorkspaceProjectStatus.Failed, result.projects[2].status);
        Assert.DoesNotContain("dependent", parsed);
        Assert.True(Directory.Exists(Path.Combine(scratch.root, "out", "independent")));
        Assert.False(Directory.Exists(Path.Combine(scratch.root, "out", "dependent")));
        Assert.False(Directory.Exists(Path.Combine(scratch.root, "out", "bad")));
    }

    [Fact]
    public async Task Executor_RespectsPrerequisitesAndConfiguredConcurrency()
    {
        using Scratch scratch = new();
        ConcurrencyProbe probe = new();
        ConcurrentQueue<string> parsed = new();
        await using BindingHost host = BindingGenerator.CreateBuilder().UseDetachedFrontend(() => new ProbeFrontend(parsed, probe: probe)).Build();
        BindingWorkspace workspace = new([Project(scratch.root, "dependent", ["first", "second"]), Project(scratch.root, "first", []), Project(scratch.root, "second", [])], TestHostPaths.Create(), parallelism: 2);
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(30));
        WorkspaceResult result = await host.GenerateWorkspaceAsync(workspace, cancellation.Token);
        Assert.True(result.success, string.Join(Environment.NewLine, result.diagnostics.Select(diagnostic => diagnostic.message)));
        Assert.Equal(2, probe.maximum);
        Assert.Equal("dependent", parsed.Last());
        Assert.All(result.projects, project => Assert.True(project.success));
    }

    [Fact]
    public async Task WorkspaceInputMutation_BlocksPublicationAfterLoading()
    {
        using Scratch scratch = new();
        File.WriteAllText(Path.Combine(scratch.root, "bindgen.json"), """
            {"schemaVersion":2,"library":{"id":"first","nativeName":"first"},"source":{"headers":["first.h"]},
             "targets":[{"id":"fixture-secondary"}],"output":{"root":"out/first"}}
            """);
        string path = Path.Combine(scratch.root, "workspace.json");
        File.WriteAllText(path, """{"schemaVersion":1,"projects":[{"id":"first","configuration":"bindgen.json"}]}""");
        BindingWorkspace workspace = new WorkspaceLoader(TestHostPaths.Create()).Load(path);
        File.AppendAllText(path, " ");
        ConcurrentQueue<string> parsed = new();
        await using BindingHost host = BindingGenerator.CreateBuilder().UseDetachedFrontend(() => new ProbeFrontend(parsed)).Build();
        WorkspaceResult result = await host.GenerateWorkspaceAsync(workspace);
        Assert.False(result.success);
        Assert.Contains(result.diagnostics, diagnostic => diagnostic.message.Contains("changed", StringComparison.Ordinal));
        Assert.Empty(parsed);
        Assert.False(Directory.Exists(Path.Combine(scratch.root, "out", "first")));
    }

    [Fact]
    public async Task Cancellation_DrainsActiveProjectsAndRemovesTheirCandidates()
    {
        using Scratch scratch = new();
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ConcurrentQueue<string> parsed = new();
        await using BindingHost host = BindingGenerator.CreateBuilder().UseDetachedFrontend(() => new ProbeFrontend(parsed, started: started)).Build();
        using CancellationTokenSource cancellation = new();
        Task<WorkspaceResult> running = host.GenerateWorkspaceAsync(new([Project(scratch.root, "first", []), Project(scratch.root, "second", []), Project(scratch.root, "third", ["first"])], TestHostPaths.Create()), cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(15));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.Empty(Directory.GetDirectories(scratch.root, ".bgcs-staging-*", SearchOption.AllDirectories));
        Assert.False(Directory.Exists(Path.Combine(scratch.root, "out", "third")));
    }

    private static WorkspaceProject Project(string root, string id, ImmutableArray<string> prerequisites)
    {
        File.WriteAllText(Path.Combine(root, id + ".h"), "int echo(void);");
        return new(id, new(new BindingConfiguration
        {
            schemaVersion = 2, library = new() { id = id, nativeName = id }, source = new() { headers = [id + ".h"] },
            targets = [new() { id = "host" }], api = new() { @namespace = "Example." + id, name = "NativeApi" },
            output = new() { root = "out/" + id }, cache = new() { enabled = false, directory = ".cache/" + id }
        }, root), prerequisites);
    }

    private sealed class ProbeFrontend(ConcurrentQueue<string> parsed, string? fail = null, ConcurrencyProbe? probe = null, TaskCompletionSource? started = null) : IFrontend
    {
        public ValueTask<FrontendPreparation> PrepareAsync(FrontendRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new FrontendPreparation(request, new([], []), []));
        }

        public async ValueTask<FrontendResult> ParseAsync(FrontendRequest request, CancellationToken cancellationToken)
        {
            string id = Path.GetFileNameWithoutExtension(request.headers[0]);
            parsed.Enqueue(id);
            if (id == fail) throw new IOException("Deliberate failed workspace input.");
            if (started is not null) { started.TrySetResult(); await Task.Delay(Timeout.Infinite, cancellationToken); }
            if (probe is not null)
            {
                int active = Interlocked.Increment(ref probe.active);
                lock (probe) probe.maximum = Math.Max(probe.maximum, active);
                if (active == 2) probe.overlap.TrySetResult();
                try { await probe.overlap.Task.WaitAsync(cancellationToken); }
                finally { Interlocked.Decrement(ref probe.active); }
            }
            NativeType integer = new(new("int"), NativeTypeCategory.Primitive, "int", 4, 4) { primitiveKind = "Int", isSigned = true };
            NativeDeclaration function = new(new("function"), "Function", "echo", "echo", null, new(request.headers[0], 1, 1))
            { returnType = integer.id, callingConvention = "C" };
            NativeModule module = new(request.target.targetId.value, request.target.triple, TestComposition.PointerSize(request.target), [integer], [function], []) { language = SourceLanguage.C, byteOrder = request.target.byteOrder };
            return new(module, new([], []));
        }
    }

    private sealed class ConcurrencyProbe
    {
        internal readonly TaskCompletionSource overlap = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int active;
        internal int maximum;
    }
    private sealed class Scratch : IDisposable
    {
        internal readonly string root = Path.Combine(Path.GetTempPath(), "bgcs-workspace-" + Guid.NewGuid().ToString("N"));
        internal Scratch() => Directory.CreateDirectory(root);
        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
