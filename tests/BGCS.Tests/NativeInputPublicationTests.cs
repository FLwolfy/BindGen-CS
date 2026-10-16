using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;
using BGCS.Infrastructure.IO;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeInputPublicationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateNativeInputMutation_CannotReplaceAnAlreadyPublishedCandidate(bool shadow)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-native-final-input-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            Directory.CreateDirectory(Path.Combine(temporary, "first"));
            Directory.CreateDirectory(Path.Combine(temporary, "second"));
            File.WriteAllText(Path.Combine(temporary, "native.h"), "#include <value.def>\npayload echo(payload value);");
            string original = Path.Combine(temporary, "second", "value.def");
            File.WriteAllText(original, "typedef int payload;");
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "inputs", nativeName = "inputs" },
                source = new() { headers = ["native.h"], includeDirectories = ["first", "second"], parseMacros = false }, cache = new() { enabled = false }
            };
            await using (BindingHost initial = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build())
                Assert.True((await initial.GenerateAsync(new(configuration, temporary))).success);
            string report = Path.Combine(temporary, "Generated", "generation.report.json");
            string previous = File.ReadAllText(report);
            string output = Path.GetDirectoryName(report)!;
            Dictionary<string, string> previousFiles = Snapshot(output);
            // Published metadata uses logical artifact paths. Capture this
            // independent physical oracle directly from the actual inputs.
            SourceSnapshot originalInputs = new ExternalInputSnapshotBuilder().Capture(
                [new(Path.Combine(temporary, "native.h"), ExternalInputKind.File), new(original, ExternalInputKind.File)], temporary) with
            {
                searchDirectories = [DirectoryMembershipDigest.Capture(Path.Combine(temporary, "first")),
                    DirectoryMembershipDigest.Capture(Path.Combine(temporary, "second"))]
            };
            Assert.Null(SourceSnapshotVerifier.GetMismatch(originalInputs));
            int mutations = 0;
            await using BindingHost mutating = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot())
                .AddValidator("mutate-input", 0, () => new MutatingValidator(() =>
                {
                    mutations++;
                    if (shadow) File.WriteAllText(Path.Combine(temporary, "first", "value.def"), "typedef float payload;");
                    else File.WriteAllText(original, "typedef long long payload;");
                })).Build();
            GenerationResult failed = await mutating.GenerateAsync(new(configuration, temporary));
            Assert.False(failed.success);
            Assert.False(failed.published);
            Assert.Equal(1, mutations);
            Assert.Equal(shadow ? "typedef int payload;" : "typedef long long payload;", File.ReadAllText(original));
            if (shadow) Assert.Equal("typedef float payload;", File.ReadAllText(Path.Combine(temporary, "first", "value.def")));
            string mismatch = shadow ? "Include or library search membership changed: " + Path.Combine(temporary, "first")
                : "Input file bytes changed: " + original;
            Assert.Equal(mismatch, SourceSnapshotVerifier.GetMismatch(originalInputs));
            BindingDiagnostic failure = Assert.Single(failed.diagnostics, diagnostic => diagnostic.isFailure);
            Assert.True(failure.message == "An extension's declared external inputs changed during generation. " + mismatch, failed.GetDiagnosticSummary());
            Assert.Equal(previous, File.ReadAllText(report));
            Assert.Equal(previousFiles.OrderBy(entry => entry.Key), Snapshot(output).OrderBy(entry => entry.Key));
            Assert.Empty(Directory.GetDirectories(temporary, ".bgcs-staging-*"));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    private static Dictionary<string, string> Snapshot(string root)
        => Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(root, path),
            path => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))), StringComparer.Ordinal);

    private sealed class MutatingValidator(Action mutation) : IGenerationValidator
    {
        public string id => "mutate-input";
        public int priority => 0;
        public ValueTask<ImmutableArray<BindingDiagnostic>> ValidateAsync(ValidationRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            mutation();
            return ValueTask.FromResult(ImmutableArray<BindingDiagnostic>.Empty);
        }
    }

    private static string WorkerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(directory!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
