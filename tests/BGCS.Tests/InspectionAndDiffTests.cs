using BGCS.Core.Plugins;
using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Core.Ports;
using BGCS.Core.Requests;
using BGCS.Infrastructure.Execution;
using Xunit;

namespace BGCS.Tests;

public sealed class InspectionAndDiffTests
{
    [Fact]
    public async Task RealInspectAndDiffCleanUpAnAliasedTemporaryRootWithoutChangingFormalOutput()
    {
        using Scratch scratch = new();
        string? repository = AppContext.BaseDirectory;
        while (repository is not null && !File.Exists(Path.Combine(repository, "BindGen-CS.slnx"))) repository = Path.GetDirectoryName(repository);
        Assert.NotNull(repository);
        string build = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        string worker = Path.Combine(repository!, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", build, "net10.0");
        string driver = Path.Combine(repository, "tests", "fixtures", "hosts", "EmbeddedSdkHost", "bin", build, "net10.0", "EmbeddedSdkHost.dll");
        string physical = Path.Combine(scratch.root, "physical-temp"), alias = Path.Combine(scratch.root, "temp-alias");
        Directory.CreateDirectory(physical);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
                string command = "$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path '" + alias.Replace("'", "''", StringComparison.Ordinal)
                    + "' -Target '" + physical.Replace("'", "''", StringComparison.Ordinal) + "' | Out-Null";
                var linked = await ProcessExecutor.ExecuteAsync(powershell,
                    ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(command))],
                    scratch.root, TimeSpan.FromSeconds(30));
                Assert.True(linked.exitCode == 0, linked.standardOutput + linked.standardError);
            }
            else Directory.CreateSymbolicLink(alias, physical);
            string inputRoot = Path.Combine(scratch.root, "inputs");
            Directory.CreateDirectory(inputRoot);
            await File.WriteAllTextAsync(Path.Combine(inputRoot, "native.h"), "int echo(void);");
            string configuration = Path.Combine(inputRoot, "bindgen.json");
            await File.WriteAllTextAsync(configuration, """
                {"schemaVersion":2,"library":{"id":"example","nativeName":"example"},"source":{"headers":["native.h"],"parseMacros":false},
                 "api":{"namespace":"Example","name":"NativeApi"},"cache":{"enabled":false}}
                """);
            string? dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            string dotnet = string.IsNullOrEmpty(dotnetRoot) ? "dotnet" : Path.Combine(dotnetRoot, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            Dictionary<string, string?> environment = new() { ["TMPDIR"] = alias, ["TEMP"] = alias, ["TMP"] = alias };
            ProcessExecutionResult child = await ProcessExecutor.ExecuteAsync(dotnet, [driver, "--inspect-and-diff", worker, configuration],
                inputRoot, TimeSpan.FromSeconds(120), environmentOverrides: environment);
            Assert.False(child.timedOut, child.standardError);
            Assert.True(child.exitCode == 0, child.standardOutput + Environment.NewLine + child.standardError);
            using JsonDocument result = JsonDocument.Parse(child.standardOutput);
            Assert.True(result.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal(Path.TrimEndingDirectorySeparator(alias), Path.TrimEndingDirectorySeparator(result.RootElement.GetProperty("tempRoot").GetString()!));
            Assert.True(result.RootElement.GetProperty("formalOutputUnchanged").GetBoolean());
            Assert.Empty(Directory.EnumerateFileSystemEntries(physical));
        }
        finally
        {
            if (new DirectoryInfo(alias).LinkTarget is not null)
            {
                if (OperatingSystem.IsWindows()) Directory.Delete(alias);
                else File.Delete(alias);
            }
        }
    }

    [Fact]
    public async Task RealHeaderDiff_KeepsLogicalOutputAndInputMembershipIdenticalToGeneration()
    {
        using Scratch scratch = new();
        string? repository = AppContext.BaseDirectory;
        while (repository is not null && !File.Exists(Path.Combine(repository, "BindGen-CS.slnx"))) repository = Path.GetDirectoryName(repository);
        Assert.NotNull(repository);
        string build = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        string worker = Path.Combine(repository!, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", build, "net10.0");
        File.WriteAllText(Path.Combine(scratch.root, "native.h"), "int native_add(int a, int b);");
        BindingConfiguration input = Configuration() with { targets = [new() { id = "host" }] };
        GenerationRequest request = new(input, scratch.root);
        await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(worker).Build();
        GenerationResult result = await host.GenerateAsync(request);
        Assert.True(result.success, result.GetDiagnosticSummary());
        var before = Directory.GetFiles(result.outputRoot, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        GenerationDiffResult diff = await host.DiffAsync(request);
        Assert.True(diff.success, string.Join(Environment.NewLine, diff.diagnostics.Select(diagnostic => diagnostic.message)));
        Assert.Empty(diff.changes);
        foreach ((string path, byte[] content) in before) Assert.Equal(content, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task Inspection_UsesTheUnifiedProjectionWithoutCreatingFormalOutput()
    {
        using Scratch scratch = new();
        await using BindingHost host = Host();
        InspectionResult result = await host.InspectAsync(new(Configuration(), scratch.root));
        Assert.True(result.success, string.Join(Environment.NewLine, result.diagnostics.Select(diagnostic => diagnostic.message)));
        TargetInspection target = Assert.Single(result.targets);
        Assert.Equal(TestComposition.HostTargetId(), target.targetId);
        Assert.Null(target.bridge);
        Assert.NotNull(target.managed);
        Assert.Same(target.interop, target.managed!.interop);
        Assert.Single(target.interop.functions);
        Assert.False(Directory.Exists(Path.Combine(scratch.root, "Generated")));
    }

    [Fact]
    public async Task Diff_ReportsFullAddRemoveAndChangeIdentitiesWithoutChangingOutput()
    {
        using Scratch scratch = new();
        await using BindingHost host = Host();
        GenerationRequest request = new(Configuration(), scratch.root);
        GenerationDiffResult first = await host.DiffAsync(request);
        Assert.True(first.success);
        Assert.True(first.hasChanges);
        Assert.All(first.changes, change => Assert.Equal(ArtifactChangeKind.Added, change.kind));
        Assert.False(Directory.Exists(first.outputRoot));
        GenerationResult published = await host.GenerateAsync(request);
        Assert.True(published.success, published.GetDiagnosticSummary());
        var before = Directory.GetFiles(published.outputRoot, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);
        GenerationRequest renamed = request.WithConfiguration(request.configuration with { api = request.configuration.api with { name = "RenamedApi" } });
        GenerationDiffResult difference = await host.DiffAsync(renamed);
        Assert.True(difference.success, string.Join(Environment.NewLine, difference.diagnostics.Select(diagnostic => diagnostic.message)));
        Assert.Contains(difference.changes, change => change.kind == ArtifactChangeKind.Added && change.relativePath.EndsWith("RenamedApi.g.cs", StringComparison.Ordinal));
        Assert.Contains(difference.changes, change => change.kind == ArtifactChangeKind.Removed && change.relativePath.EndsWith("NativeApi.g.cs", StringComparison.Ordinal));
        GenerationRequest renamedMethod = request.WithConfiguration(request.configuration with
        { api = request.configuration.api with { names = ImmutableDictionary<string, string>.Empty.Add("echo", "EchoRenamed") } });
        GenerationDiffResult changed = await host.DiffAsync(renamedMethod);
        Assert.True(changed.success);
        Assert.Contains(changed.changes, change => change.kind == ArtifactChangeKind.Changed && change.relativePath.EndsWith("NativeApi.Methods.g.cs", StringComparison.Ordinal));
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), Directory.GetFiles(published.outputRoot, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
        foreach ((string path, byte[] bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task Diff_ExplainsBoundedNonCacheMetadataChangesWithoutChangingOutput()
    {
        using Scratch scratch = new();
        const string oldPrefix = "previous-validator-";
        const string newPrefix = "current-validator-";
        string oldMessage = oldPrefix + new string('x', 240);
        string newMessage = newPrefix + new string('y', 240);
        await using BindingHost original = BindingGenerator.CreateBuilder().UseDetachedFrontend(() => new ScalarFrontend())
            .RegisterFactory<IGenerationValidator>(new("metadata-probe", ExtensionPort.GenerationValidator, 0), _ => new MetadataValidator(0, oldMessage)).Build();
        await using BindingHost changed = BindingGenerator.CreateBuilder().UseDetachedFrontend(() => new ScalarFrontend())
            .RegisterFactory<IGenerationValidator>(new("metadata-probe", ExtensionPort.GenerationValidator, 7), _ => new MetadataValidator(7, newMessage)).Build();
        GenerationRequest request = new(Configuration(), scratch.root);
        GenerationResult published = await original.GenerateAsync(request);
        Assert.True(published.success, published.GetDiagnosticSummary());
        var before = Directory.GetFiles(published.outputRoot, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);

        GenerationDiffResult difference = await changed.DiffAsync(request);
        Assert.True(difference.success, string.Join(Environment.NewLine, difference.diagnostics.Select(diagnostic => diagnostic.message)));
        Assert.Contains(difference.changes, change => change.kind == ArtifactChangeKind.Changed && change.relativePath.EndsWith("/metadata/verification.json", StringComparison.Ordinal));
        Assert.Contains(difference.changes, change => change.kind == ArtifactChangeKind.Changed && change.relativePath == "generation.report.json");
        BindingDiagnostic verification = Assert.Single(difference.diagnostics.Where(diagnostic => diagnostic.code == "BGCS-DIFF-METADATA"
            && diagnostic.message.StartsWith("Metadata changed in '" + TestComposition.HostTargetId() + "/metadata/verification.json'", StringComparison.Ordinal)));
        Assert.Equal(BindingDiagnosticSeverity.Information, verification.severity);
        Assert.Contains("$[\"validationGates\"][0][\"priority\"]: 0 -> 7", verification.message);
        BindingDiagnostic report = Assert.Single(difference.diagnostics.Where(diagnostic => diagnostic.code == "BGCS-DIFF-METADATA"
            && diagnostic.message.StartsWith("Metadata changed in 'generation.report.json'", StringComparison.Ordinal)));
        Assert.Equal(BindingDiagnosticSeverity.Information, report.severity);
        Assert.Contains("[\"message\"]:", report.message);
        Assert.Contains(oldPrefix, report.message);
        Assert.Contains(newPrefix, report.message);
        Assert.DoesNotContain(oldMessage, report.message);
        Assert.DoesNotContain(newMessage, report.message);
        string[] lines = report.message.Split(Environment.NewLine);
        Assert.Equal(13, lines.Length);
        foreach (string line in lines.Skip(1))
        {
            string[] values = line[(line.IndexOf(": ", StringComparison.Ordinal) + 2)..].Split(" -> ", StringSplitOptions.None);
            Assert.Equal(2, values.Length);
            Assert.All(values, value => Assert.InRange(value.Length, 1, 180));
        }
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), Directory.GetFiles(published.outputRoot, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
        foreach ((string path, byte[] bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task Diff_RejectsCorruptOrUnownedPreviousOutput()
    {
        using Scratch scratch = new();
        await using BindingHost host = Host();
        GenerationRequest request = new(Configuration(), scratch.root);
        GenerationResult generated = await host.GenerateAsync(request);
        Assert.True(generated.success, generated.GetDiagnosticSummary());
        string managed = Path.Combine(generated.outputRoot, generated.artifacts.First(artifact => artifact.mediaType == "text/x-csharp").relativePath);
        File.AppendAllText(managed, "\n// corruption");
        byte[] corrupt = File.ReadAllBytes(managed);
        GenerationDiffResult result = await host.DiffAsync(request);
        Assert.False(result.success);
        Assert.Empty(result.changes);
        Assert.Contains(result.diagnostics, diagnostic => diagnostic.code == "BGCS-DIFF-OUTPUT");
        Assert.Equal(corrupt, File.ReadAllBytes(managed));
        File.Delete(Path.Combine(generated.outputRoot, ".bgcs-artifacts.json"));
        GenerationDiffResult unowned = await host.DiffAsync(request);
        Assert.False(unowned.success);
        Assert.Contains(unowned.diagnostics, diagnostic => diagnostic.message.Contains("ownership", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BridgeOnly_DoesNotActivateManagedProjectionOrBackend()
    {
        using Scratch scratch = new();
        string? repository = AppContext.BaseDirectory;
        while (repository is not null && !File.Exists(Path.Combine(repository, "BindGen-CS.slnx"))) repository = Path.GetDirectoryName(repository);
        Assert.NotNull(repository);
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        string worker = Path.Combine(repository!, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
        File.WriteAllText(Path.Combine(scratch.root, "native.hpp"), "inline int add(int a, int b) { return a+b; }");
        BindingConfiguration input = Configuration() with { source = new() { language = SourceLanguage.Cpp, headers = ["native.hpp"], parseMacros = false }, targets = [new() { id = "host" }] };
        await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(worker)
            .RegisterFactory<IManagedProjection>(new("fixture.projection", ExtensionPort.ManagedProjection), _ => throw new InvalidOperationException("The bridge must not project managed APIs."))
            .RegisterFactory<IBindingBackend>(new("fixture.backend", ExtensionPort.BindingBackend), _ => throw new InvalidOperationException("The bridge must not emit managed source.")).Build();
        GenerationResult result = await host.BridgeAsync(new(input, scratch.root));
        Assert.True(result.success, result.GetDiagnosticSummary());
        Assert.Contains(result.artifacts, artifact => artifact.mediaType == "text/x-c++");
        Assert.DoesNotContain(result.artifacts, artifact => artifact.mediaType == "text/x-csharp");
        TargetInspection inspected = Assert.Single(result.targetModels);
        Assert.NotNull(inspected.bridge);
        Assert.Null(inspected.managed);
        Assert.NotEmpty(inspected.interop.functions);
        Assert.All(result.artifacts.Where(artifact => artifact.mediaType == "application/json"), artifact =>
            Assert.DoesNotContain(".bgcs-staging-", File.ReadAllText(Path.Combine(result.outputRoot, artifact.relativePath))));
    }

    private static BindingHost Host() => BindingGenerator.CreateBuilder().UseDetachedFrontend(() => new ScalarFrontend()).Build();
    private static BindingConfiguration Configuration() => new()
    {
        schemaVersion = 2,
        library = new() { id = "example", nativeName = "example" },
        source = new() { headers = ["native.h"] },
        targets = [new() { id = "host" }],
        api = new() { @namespace = "Example", name = "NativeApi" },
        cache = new() { enabled = false }
    };
    private sealed class MetadataValidator(
        int order,
        string message
    ) : IGenerationValidator
    {
        public string id => "metadata-probe";
        public int priority => order;
        public ValueTask<ImmutableArray<BindingDiagnostic>> ValidateAsync(
            ValidationRequest request,
            CancellationToken cancellationToken
        ) {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Enumerable.Range(0, 20).Select(index => new BindingDiagnostic(BindingDiagnosticSeverity.Information, message + index)).ToImmutableArray());
        }
    }
    private sealed class ScalarFrontend : IFrontend
    {
        public ValueTask<FrontendPreparation> PrepareAsync(
            FrontendRequest request,
            CancellationToken cancellationToken
        ) {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new FrontendPreparation(request, new([], []), []));
        }

        public ValueTask<FrontendResult> ParseAsync(
            FrontendRequest request,
            CancellationToken cancellationToken
        ) {
            NativeType integer = new(new("int"), NativeTypeCategory.Primitive, "int", 4, 4) { primitiveKind = "Int", isSigned = true };
            NativeDeclaration function = new(new("function"), "Function", "echo", "echo", null, new(request.headers[0], 1, 1)) { returnType = integer.id, callingConvention = "C" };
            return ValueTask.FromResult(new FrontendResult(new(request.target.targetId.value, request.target.triple, TestComposition.PointerSize(request.target), [integer], [function], []) { language = SourceLanguage.C, byteOrder = request.target.byteOrder }, new([], [])));
        }
    }
    private sealed class Scratch : IDisposable
    {
        internal readonly string root = Path.Combine(Path.GetTempPath(), "bgcs-inspection-test-" + Guid.NewGuid().ToString("N"));
        internal Scratch()
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "native.h"), "int echo(void);");
        }
        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
