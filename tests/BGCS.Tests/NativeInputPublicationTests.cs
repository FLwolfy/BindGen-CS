using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;
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
            await using BindingHost mutating = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot())
                .AddValidator("mutate-input", 0, () => new MutatingValidator(() =>
                {
                    if (shadow) File.WriteAllText(Path.Combine(temporary, "first", "value.def"), "typedef float payload;");
                    else File.WriteAllText(original, "typedef long long payload;");
                })).Build();
            GenerationResult failed = await mutating.GenerateAsync(new(configuration, temporary));
            Assert.False(failed.success);
            Assert.Contains("Native file bytes or include-search membership changed", failed.GetDiagnosticSummary());
            Assert.Equal(previous, File.ReadAllText(report));
            Assert.Empty(Directory.GetDirectories(temporary, ".bgcs-staging-*"));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

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
