using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.Infrastructure.IO;
using BGCS.NativeBuild.Execution;
using BGCS.NativeBuild.Planning;
using BGCS.Platforms.Targeting;
using Xunit;

namespace BGCS.NativeBuild.Tests;

public sealed class NativePreprocessingFenceTests
{
    [Fact]
    public async Task TwoRealPreprocessingPassesPreserveTokensMacrosAndIndependentDependencies()
    {
        using Probe fixture = new();
        NativeBuildPlan plan = fixture.TwoPassPlan();

        NativeBuildExecutionResult result = await new PlannedNativeBuildExecutor().PreprocessWithDependenciesAsync(plan, TimeSpan.FromSeconds(60));

        Assert.Equal(3, result.executions.Length); // Independent scan, tokens, final macros.
        Assert.All(result.executions, execution => { Assert.Equal(0, execution.exitCode); Assert.False(execution.timedOut); });
        Assert.Contains("native_fence_fixture", await File.ReadAllTextAsync(plan.preprocessingOperations[0].outputFile));
        string macros = await File.ReadAllTextAsync(plan.preprocessingOperations[1].outputFile);
        Assert.Contains("#define NATIVE_VALUE 7", macros);
        Assert.Contains("#define ARGUMENT_VALUE 13", macros);
        Assert.DoesNotContain("native_fence_fixture", macros);
        Assert.Contains(result.inputSnapshot.files, input => input.path == fixture.header);
        Assert.Equal(fixture.response, Assert.Single(result.argumentInputs.files).path);
        Assert.True(SourceSnapshotVerifier.Matches(result.inputSnapshot));
        Assert.True(SourceSnapshotVerifier.Matches(result.argumentInputs));
        Assert.False(File.Exists(plan.outputFile));
    }

    [Fact]
    public async Task OnlySelectedProofCallsTheVerifierAndOrdinaryConstructionStillCompletes()
    {
        using Probe fixture = new(additionalFacts: true);
        int callbacks = 0;
        NativeBuildExecutionResult result = await new PlannedNativeBuildExecutor().ExecuteWithDependenciesAsync(fixture.plan, TimeSpan.FromSeconds(60),
            sourceVerifier: async (operation, token) =>
            {
                callbacks++;
                Assert.Equal(fixture.source, operation.source);
                Assert.Contains("native_fence_fixture", await File.ReadAllTextAsync(operation.outputFile, token));
            }, verificationSources: [fixture.source]);

        Assert.Equal(1, callbacks);
        Assert.All(result.executions, execution => Assert.Equal(0, execution.exitCode));
        Assert.True(File.Exists(fixture.plan.outputFile));
        Assert.Contains(result.inputSnapshot.files, input => input.path == fixture.facts);
        Assert.True(SourceSnapshotVerifier.Matches(result.inputSnapshot));
    }

    [Theory]
    [InlineData("header")]
    [InlineData("response")]
    [InlineData("expanded-source")]
    [InlineData("dependency-output")]
    public async Task CallbackMutationCannotCrossThePostProofFence(string mutation)
    {
        using Probe fixture = new();
        int callbacks = 0;
        IOException failure = await Assert.ThrowsAsync<IOException>(() => new PlannedNativeBuildExecutor().ExecuteWithDependenciesAsync(
            fixture.plan, TimeSpan.FromSeconds(60), sourceVerifier: async (operation, token) =>
            {
                callbacks++;
                Assert.Contains("native_fence_fixture", await File.ReadAllTextAsync(operation.outputFile, token));
                string path = mutation switch
                {
                    "header" => fixture.header,
                    "response" => fixture.response,
                    "expanded-source" => operation.outputFile,
                    _ => operation.dependencyFile!.path
                };
                DateTime timestamp = File.GetLastWriteTimeUtc(path);
                string text = await File.ReadAllTextAsync(path, token);
                string changed = mutation switch
                {
                    "header" => text.Replace("VALUE 7", "VALUE 8", StringComparison.Ordinal),
                    "response" => text.Replace("VALUE=13", "VALUE=14", StringComparison.Ordinal),
                    _ => text + "\nchanged after detached verification\n"
                };
                Assert.NotEqual(text, changed);
                await File.WriteAllTextAsync(path, changed, token);
                File.SetLastWriteTimeUtc(path, timestamp);
            }, verificationSources: [fixture.source]));

        Assert.Equal(1, callbacks);
        Assert.Contains("changed during detached fact verification", failure.Message);
        Assert.False(File.Exists(fixture.plan.outputFile));
    }

    [Fact]
    public async Task NativeInputChangedAfterPreprocessingIsRejectedBeforeCallbackEntry()
    {
        using Probe fixture = new();
        NativePreprocessingOperation original = Assert.Single(fixture.plan.preprocessingOperations);
        string stamp = File.GetLastWriteTimeUtc(fixture.header).ToString("O", CultureInfo.InvariantCulture);
        NativeBuildCommand wrapped = fixture.AfterCompiler(original.command,
            "[IO.File]::WriteAllText(" + PowerShell(fixture.header) + ", '#define NATIVE_VALUE 8' + [Environment]::NewLine); "
                + "[IO.File]::SetLastWriteTimeUtc(" + PowerShell(fixture.header) + ", [DateTime]::Parse(" + PowerShell(stamp) + ", [Globalization.CultureInfo]::InvariantCulture));",
            "printf '#define NATIVE_VALUE 8\\n' > " + Shell(fixture.header));
        NativeBuildPlan plan = fixture.plan with { preprocessingOperations = [original with { command = wrapped }] };
        int callbacks = 0;

        IOException failure = await Assert.ThrowsAsync<IOException>(() => new PlannedNativeBuildExecutor().ExecuteWithDependenciesAsync(plan,
            TimeSpan.FromSeconds(60), sourceVerifier: (_, _) => { callbacks++; return ValueTask.CompletedTask; }, verificationSources: [fixture.source]));

        Assert.Equal(0, callbacks);
        Assert.Contains("changed while their effective declarations were preprocessed", failure.Message);
        Assert.False(File.Exists(plan.outputFile));
    }

    [Fact]
    public async Task SecondActualPreprocessingPassCannotChangeTheIndependentDependencyClosure()
    {
        using Probe fixture = new();
        NativeBuildPlan plan = fixture.TwoPassPlan();
        NativePreprocessingOperation second = plan.preprocessingOperations[1];
        string extra = Path.Combine(fixture.root, "extra.h");
        File.WriteAllText(extra, "/* not read by the compiler */");
        // A valid extra Make dependency must still be rejected, even though it
        // names an existing file and both actual compiler operations succeeded.
        string append = " " + extra.Replace('\\', '/').Replace(" ", "\\ ", StringComparison.Ordinal);
        NativeBuildCommand wrapped = fixture.AfterCompiler(second.command,
            "[IO.File]::AppendAllText(" + PowerShell(second.dependencyFile!.path) + ", " + PowerShell(append) + ");",
            "printf '%s' " + Shell(append) + " >> " + Shell(second.dependencyFile!.path));
        plan = plan with { preprocessingOperations = [plan.preprocessingOperations[0], second with { command = wrapped }] };

        IOException failure = await Assert.ThrowsAsync<IOException>(() => new PlannedNativeBuildExecutor().PreprocessWithDependenciesAsync(plan, TimeSpan.FromSeconds(60)));

        Assert.Contains("dependency closure differs from its independent preflight scan", failure.Message);
        Assert.False(File.Exists(plan.outputFile));
    }

    [Fact]
    public async Task CallerCancellationAfterBothActualPreprocessingOutputsStillPreventsReturn()
    {
        using Probe fixture = new();
        NativeBuildPlan plan = fixture.TwoPassPlan();
        NativePreprocessingOperation second = plan.preprocessingOperations[1];
        string entered = Path.Combine(fixture.root, "second-pass-completed");
        NativeBuildCommand wrapped = fixture.AfterCompiler(second.command,
            "[IO.File]::WriteAllText(" + PowerShell(entered) + ", 'ready'); Start-Sleep -Seconds 30;",
            "printf ready > " + Shell(entered) + "\nsleep 30");
        plan = plan with { preprocessingOperations = [plan.preprocessingOperations[0], second with { command = wrapped }] };
        using CancellationTokenSource caller = new();
        Task<NativeBuildExecutionResult> execution = new PlannedNativeBuildExecutor().PreprocessWithDependenciesAsync(plan, TimeSpan.FromSeconds(60), caller.Token);
        try
        {
            Stopwatch wait = Stopwatch.StartNew();
            while (!File.Exists(entered) && !execution.IsCompleted && wait.Elapsed < TimeSpan.FromSeconds(20))
                await Task.Delay(20);
            Assert.True(File.Exists(entered), execution.IsCompleted ? "The native operation ended before the second-pass barrier." : "The real second preprocessing pass did not reach its barrier.");
            Assert.All(plan.preprocessingOperations, operation => Assert.True(File.Exists(operation.outputFile)));
            caller.Cancel();

            OperationCanceledException failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
            Assert.Equal(caller.Token, failure.CancellationToken);
            Assert.False(File.Exists(plan.outputFile));
        }
        finally
        {
            caller.Cancel();
            try { await execution; }
            catch (Exception) { } // Reap the complete child tree before deleting the owned fixture.
        }
    }

    private static string PowerShell(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    private static string Shell(string value) => "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";

    private sealed class Probe : IDisposable
    {
        internal string root { get; } = Path.Combine(PhysicalPathResolver.Resolve(Path.GetTempPath()), "bgcs native PP fences " + Guid.NewGuid().ToString("N"));
        internal string header { get; }
        internal string source { get; }
        internal string response { get; }
        internal string facts { get; }
        internal NativeBuildPlan plan { get; }
        private int m_hook;

        internal Probe(bool additionalFacts = false)
        {
            Directory.CreateDirectory(root);
            header = Path.Combine(root, "original.h"); source = Path.Combine(root, "native.c"); response = Path.Combine(root, "arguments.rsp"); facts = Path.Combine(root, "facts.c");
            File.WriteAllText(header, "#define NATIVE_VALUE 7\n");
            File.WriteAllText(source, "#include \"original.h\"\nint native_fence_fixture(void) { return NATIVE_VALUE + ARGUMENT_VALUE; }\n");
            File.WriteAllText(response, "-DARGUMENT_VALUE=13\n");
            File.WriteAllText(facts, "#include \"original.h\"\nconst int detached_fence_fact = NATIVE_VALUE + ARGUMENT_VALUE;\n");
            string compiler = NativeBuildToolDiscovery.FindExecutable("clang", OperatingSystem.IsWindows() ? "C:/Program Files/LLVM/bin/clang.exe" : null)
                ?? throw new InvalidOperationException("Preprocessing fence regression tests require the actual installed Clang driver.");
            string scratch = Path.Combine(root, "scratch");
            Directory.CreateDirectory(scratch);
            NativeTargetDescriptor identity = new TargetResolver().Resolve(new(new("host"), new(compilerPath: compiler)));
            // This fixture tests proof fences using only its own integer header.
            // Keep the actual host ABI and selected driver without silently
            // importing the platform provider's default SDK into this scope.
            NativeTargetDescriptor target = new(identity.targetId, identity.platformId, identity.architectureId, identity.abiId, identity.triple,
                new NativeToolchainDescriptor(compilerPath: compiler), identity.capabilities, identity.byteOrder);
            NativeBuildRequest request = new(target, [new(source, SourceLanguage.C, "c17")], scratch,
                Path.Combine(scratch, OperatingSystem.IsWindows() ? "native.obj" : "native.o"))
            {
                outputKind = NativeOutputKind.Object,
                compilerArguments = ["-nostdinc", "@" + response],
                linkerArguments = ["-nostdlib"],
                preprocessingSources = additionalFacts ? [new(facts, SourceLanguage.C, "c17")] : []
            };
            plan = new CompilerBuildProvider("clang", compiler).Plan(request, default);
            Assert.Equal(root, Assert.Single(plan.includeSearchDirectories));
            Assert.Empty(plan.argumentInputs.searchDirectories);
        }

        internal NativeBuildPlan TwoPassPlan()
        {
            NativePreprocessingOperation original = Assert.Single(plan.preprocessingOperations);
            string macros = Path.Combine(plan.scratchDirectory!, "macros.preprocessed.c"), dependencies = macros + ".d";
            NativePreprocessingOperation second = original with
            {
                outputFile = macros,
                dependencyFile = original.dependencyFile! with { path = dependencies },
                command = original.command with
                {
                    id = "preprocess-final-macros",
                    arguments = original.command.arguments.Select(argument => argument == original.outputFile ? macros
                        : argument == original.dependencyFile!.path ? dependencies : argument).ToImmutableArray().Add("-dM"),
                    outputFiles = [macros, dependencies]
                }
            };
            return plan with { preprocessingOperations = [original, second] };
        }

        internal NativeBuildCommand AfterCompiler(NativeBuildCommand command, string windows, string unix)
        {
            if (OperatingSystem.IsWindows())
            {
                string executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
                string program = "$ErrorActionPreference='Stop'; & " + PowerShell(command.executable) + " @(" + string.Join(",", command.arguments.Select(PowerShell))
                    + "); if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; " + windows;
                return command with { executable = executable, arguments = ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(program))] };
            }
            string script = Path.Combine(root, "after-compiler-" + (++m_hook).ToString(CultureInfo.InvariantCulture) + ".sh");
            File.WriteAllText(script, "\"$@\"\nresult=$?\nif [ \"$result\" -ne 0 ]; then exit \"$result\"; fi\n" + unix + "\n", new UTF8Encoding(false));
            return command with { executable = "/bin/sh", arguments = ImmutableArray.Create(script, command.executable).AddRange(command.arguments), inputFiles = command.inputFiles.Add(script) };
        }

        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
