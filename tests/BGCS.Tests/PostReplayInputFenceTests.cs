using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;
using BGCS.Infrastructure.Workers;
using Xunit;

namespace BGCS.Tests;

public sealed class PostReplayInputFenceTests
{
    private const string C_ORIGINAL = "#pragma once\nint original(void);\n";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosedStoreChecksKeepTheirPrecedenceOverParsedOnlyMutationsAndPreserveTheCompletePublication(bool addUnlistedStoreMember)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-final-input-order-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        State state = new(Path.Combine(root, "original.h"), addUnlistedStoreMember);
        try
        {
            await File.WriteAllTextAsync(state.header, C_ORIGINAL);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "fence-order-" + Guid.NewGuid().ToString("N"), nativeName = "fence_order" },
                source = new() { language = SourceLanguage.C, headers = ["original.h"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
                api = new() { @namespace = "Generated.FenceOrder", name = "FenceApi" },
                runtime = new() { mode = "source" }, cache = new() { enabled = false }
            };
            // The public worker-backed wrapper exposes the contributed store
            // path while keeping the original header a parsed-only input.
            // Existing native-view integration cases cover real replay calls.
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns()
                .WithFrontendFactory(() => new ObservingFrontend(WorkerRoot(), state))
                .AddSourceContributor("fence.order", 0, () => new Contributor())
                .AddValidator("fence-order-mutation", 0, () => new MutationValidator(state)).Build();
            GenerationRequest request = new(configuration, root);
            GenerationResult first = await host.GenerateAsync(request);
            Assert.True(first.success, first.GetDiagnosticSummary());
            Dictionary<string, string> previous = Snapshot(first.outputRoot);
            Assert.NotEmpty(previous);
            state.armed = true;
            GenerationResult rejected = await host.GenerateAsync(request);
            Assert.False(rejected.success);
            Assert.False(rejected.published);
            Assert.Empty(rejected.artifacts);
            Assert.Equal(1, state.mutations);
            Assert.Equal("#pragma once\nlong long original(void);\n", File.ReadAllText(state.header));
            BindingDiagnostic firstFailure = rejected.diagnostics.First(diagnostic => diagnostic.isFailure);
            string expected = addUnlistedStoreMember
                ? "A generated input store contains an undeclared or missing file."
                : "Native file bytes or include-search membership changed during generation; the candidate cannot be published. Input file bytes changed: " + state.header;
            Assert.True(firstFailure.message == expected, rejected.GetDiagnosticSummary());
            if (addUnlistedStoreMember) Assert.Equal("unlisted", File.ReadAllText(state.UnlistedPath()));
            Assert.Equal(previous.OrderBy(entry => entry.Key), Snapshot(first.outputRoot).OrderBy(entry => entry.Key));
            Assert.Empty(Directory.GetDirectories(root, ".bgcs-staging-*"));

            // Repair only this fixture's exact introduced member. Successful
            // retry also proves that failure released the owned store lease.
            state.armed = false;
            if (addUnlistedStoreMember) File.Delete(state.UnlistedPath());
            await File.WriteAllTextAsync(state.header, C_ORIGINAL);
            GenerationResult recovered = await host.GenerateAsync(request);
            Assert.True(recovered.success, recovered.GetDiagnosticSummary());
            Assert.Equal(1, state.mutations);
            Assert.False(Directory.Exists(state.StoreRoot()));
        }
        finally
        {
            if (state.contributedFile is string file)
            {
                string store = state.StoreRoot();
                string unlisted = state.UnlistedPath();
                if (File.Exists(unlisted)) File.Delete(unlisted);
                if (File.Exists(file)) File.Delete(file);
                for (string? directory = Path.GetDirectoryName(file); directory is not null; directory = Path.GetDirectoryName(directory))
                {
                    if (Directory.Exists(directory)) Directory.Delete(directory, recursive: false);
                    if (directory == store) break;
                }
            }
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class State(string header, bool addUnlistedMember)
    {
        internal readonly string header = header;
        internal readonly bool addUnlistedMember = addUnlistedMember;
        internal bool armed;
        internal int mutations;
        internal string? contributedFile;

        internal string StoreRoot()
        {
            string file = Assert.IsType<string>(contributedFile);
            string suffix = Path.DirectorySeparatorChar + Path.Combine("native", "include", "contributions", "fence.order", "generated.h");
            Assert.EndsWith(suffix, file, StringComparison.Ordinal);
            string store = file[..^suffix.Length];
            Assert.Equal(64, Path.GetFileName(store).Length);
            Assert.All(Path.GetFileName(store), character => Assert.True(char.IsAsciiHexDigit(character)));
            Assert.Equal(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "BGCS", "source-inputs", "v1")), Path.GetDirectoryName(store));
            return store;
        }

        internal string UnlistedPath() => Path.Combine(StoreRoot(), "unlisted-member.dat");
    }

    private sealed class Contributor : ISourceContributor
    {
        public string id => "fence.order";
        public int priority => 0;
        public ValueTask<SourceContribution> ContributeAsync(SourceContributionRequest request, CancellationToken cancellationToken)
            => ValueTask.FromResult(new SourceContribution([SourceContributionArtifact.FromUtf8("generated.h", SourceContributionRole.PublicHeader,
                "text/x-c-header", "#pragma once\nint contributed(void);\n", SourceLanguage.C, exposeToBindings: true)], new(EvidenceLevel.Declared, "final input order fixture")));
    }

    private sealed class ObservingFrontend(string workerRoot, State state) : IFrontend
    {
        private readonly FrontendWorkerClient m_worker = new(Path.Combine(workerRoot, "BGCS.Frontend.Clang.Worker.dll"));
        public async ValueTask<FrontendResult> ParseAsync(FrontendRequest request, CancellationToken cancellationToken)
        {
            string file = request.sourcePathMappings.Single(pair => pair.Value == "contributions/fence.order/generated.h").Key;
            if (state.contributedFile is string previous) Assert.Equal(previous, file);
            state.contributedFile = file;
            _ = state.StoreRoot();
            return await m_worker.ParseAsync(request, cancellationToken);
        }
    }

    private sealed class MutationValidator(State state) : IGenerationValidator
    {
        public string id => "fence-order-mutation";
        public int priority => 0;
        public ValueTask<ImmutableArray<BindingDiagnostic>> ValidateAsync(ValidationRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (state.armed)
            {
                state.mutations++;
                Assert.Equal(C_ORIGINAL, File.ReadAllText(state.header));
                File.WriteAllText(state.header, "#pragma once\nlong long original(void);\n");
                if (state.addUnlistedMember) File.WriteAllText(state.UnlistedPath(), "unlisted");
            }
            return ValueTask.FromResult(ImmutableArray<BindingDiagnostic>.Empty);
        }
    }

    private static Dictionary<string, string> Snapshot(string root)
        => Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(root, path),
            path => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))), StringComparer.Ordinal);

    private static string WorkerRoot()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "BindGen-CS.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(root.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
