using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.NativeBuild.Execution;
using BGCS.NativeBuild.Planning;
using BGCS.Platforms.Targeting;
using Xunit;

namespace BGCS.NativeBuild.Tests;

public sealed class NativeProofDeadlineTests
{
    [Fact]
    public async Task NativeDeadlineCancelsEffectiveFactVerificationAndPreventsCompilation()
    {
        using Probe fixture = new();
        bool entered = false;
        Stopwatch elapsed = Stopwatch.StartNew();
        TimeoutException failure = await Assert.ThrowsAsync<TimeoutException>(() => new PlannedNativeBuildExecutor().ExecuteWithDependenciesAsync(
            fixture.plan, TimeSpan.FromSeconds(5), sourceVerifier: async (operation, cancellationToken) =>
            {
                await fixture.CheckPreprocessedSourceAsync(operation, cancellationToken);
                entered = true;
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }, verificationSources: [fixture.source]));
        Assert.True(entered);
        Assert.Contains("effective-source fact verification", failure.Message);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(20), "The cooperative proof outlived its native construction deadline.");
        Assert.False(File.Exists(fixture.plan.outputFile));
    }

    [Fact]
    public async Task CallerCancellationDuringEffectiveFactsRemainsCallerCancellation()
    {
        using Probe fixture = new();
        using CancellationTokenSource caller = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<NativeBuildExecutionResult> execution = new PlannedNativeBuildExecutor().ExecuteWithDependenciesAsync(fixture.plan, TimeSpan.FromSeconds(30), caller.Token,
            sourceVerifier: async (operation, cancellationToken) =>
            {
                await fixture.CheckPreprocessedSourceAsync(operation, cancellationToken);
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }, verificationSources: [fixture.source]);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            caller.Cancel();
            OperationCanceledException failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
            Assert.Equal(caller.Token, failure.CancellationToken);
            Assert.False(File.Exists(fixture.plan.outputFile));
        }
        finally
        {
            caller.Cancel();
            try { await execution; }
            catch (Exception) { } // Observe completion before removing only this test's private inputs.
        }
    }

    private sealed class Probe : IDisposable
    {
        private readonly string m_root = Path.Combine(Path.GetTempPath(), "bgcs native proof deadline " + Guid.NewGuid().ToString("N"));
        internal string source { get; }
        internal NativeBuildPlan plan { get; }

        internal Probe()
        {
            Directory.CreateDirectory(m_root);
            source = Path.Combine(m_root, "native.c");
            File.WriteAllText(source, "int native_deadline_fixture(int value) { return value + 1; }\n");
            string compiler = NativeBuildToolDiscovery.FindExecutable("clang", OperatingSystem.IsWindows() ? "C:/Program Files/LLVM/bin/clang.exe" : null)
                ?? throw new InvalidOperationException("The effective-source deadline gate requires its real installed Clang compiler.");
            string scratch = Path.Combine(m_root, "scratch");
            Directory.CreateDirectory(scratch);
            NativeTargetDescriptor identity = new TargetResolver().Resolve(new(new("host")));
            // This self-contained object proves callback cancellation, independently
            // of SDK header discovery covered by the native provider matrix.
            NativeTargetDescriptor target = new(identity.targetId, identity.platformId, identity.architectureId, identity.abiId, identity.triple,
                new(compilerPath: compiler), identity.capabilities, identity.byteOrder);
            NativeBuildRequest request = new(target, [new(source, SourceLanguage.C, "c17")], scratch,
                Path.Combine(scratch, OperatingSystem.IsWindows() ? "native.obj" : "native.o"))
            { outputKind = NativeOutputKind.Object, compilerArguments = ["-nostdinc"], linkerArguments = ["-nostdlib"] };
            plan = new CompilerBuildProvider("clang", compiler).Plan(request, CancellationToken.None);
            Assert.Equal(m_root, Assert.Single(plan.includeSearchDirectories));
            Assert.Empty(plan.argumentInputs.searchDirectories);
        }

        internal async ValueTask CheckPreprocessedSourceAsync(NativePreprocessingOperation operation, CancellationToken cancellationToken)
        {
            Assert.Equal(source, operation.source);
            Assert.True(File.Exists(operation.outputFile));
            Assert.Contains("native_deadline_fixture", await File.ReadAllTextAsync(operation.outputFile, cancellationToken));
        }

        public void Dispose() => Directory.Delete(m_root, recursive: true);
    }
}
