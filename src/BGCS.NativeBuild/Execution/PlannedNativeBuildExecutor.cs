using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Infrastructure.Execution;
using BGCS.Infrastructure.IO;

namespace BGCS.NativeBuild.Execution;

/// <summary>Executes immutable native plans under one deadline and cancellation boundary.</summary>
public sealed class PlannedNativeBuildExecutor
{
    /// <summary>Runs every ordered command and rejects missing or wrong-architecture output.</summary>
    /// <param name="plan">The shell-free native build plan.</param>
    /// <param name="timeout">The shared compiler and linker deadline.</param>
    /// <param name="cancellationToken">Cancels tools and terminates their process trees.</param>
    /// <returns>Complete per-operation execution diagnostics.</returns>
    public async Task<ImmutableArray<ProcessExecutionResult>> ExecuteAsync(
        NativeBuildPlan plan,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    )
        => (await ExecuteWithDependenciesAsync(plan, timeout, cancellationToken).ConfigureAwait(false)).executions;

    /// <summary>Validates compiler-authored dependencies before and after all native operations.</summary>
    /// <param name="plan">The complete immutable native build plan.</param>
    /// <param name="timeout">One deadline covering dependency scans and construction.</param>
    /// <param name="cancellationToken">Cancels tools, hashing and directory traversal.</param>
    /// <param name="sourceVerifier">An optional host-owned detached fact verifier run before native compilation.</param>
    /// <param name="verificationSources">The explicit compilation units whose effective declarations require proof.</param>
    /// <returns>Complete diagnostics and the dependency snapshot for final publication verification.</returns>
    public async Task<NativeBuildExecutionResult> ExecuteWithDependenciesAsync(
        NativeBuildPlan plan,
        TimeSpan timeout,
        CancellationToken cancellationToken = default,
        Func<NativePreprocessingOperation, CancellationToken, ValueTask>? sourceVerifier = null,
        ImmutableArray<string> verificationSources = default
    )
        => await ExecuteCoreAsync(plan, timeout, cancellationToken, sourceVerifier, verificationSources, preprocessOnly: false).ConfigureAwait(false);

    /// <summary>Runs only the planned native preprocessing operations with independent dependency scans and input verification.</summary>
    /// <param name="plan">A provider-authored plan containing exact native preprocessing commands.</param>
    /// <param name="timeout">One deadline covering dependency discovery and preprocessing.</param>
    /// <param name="cancellationToken">Cancels compiler processes and dependency hashing.</param>
    /// <returns>Preprocessing executions and original input snapshots; no object or linked artifact is claimed.</returns>
    public Task<NativeBuildExecutionResult> PreprocessWithDependenciesAsync(
        NativeBuildPlan plan,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    )
        => ExecuteCoreAsync(plan, timeout, cancellationToken, null, [], preprocessOnly: true);

    private async Task<NativeBuildExecutionResult> ExecuteCoreAsync(
        NativeBuildPlan plan,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        Func<NativePreprocessingOperation, CancellationToken, ValueTask>? sourceVerifier,
        ImmutableArray<string> verificationSources,
        bool preprocessOnly
    ) {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.commands.IsDefaultOrEmpty) throw new ArgumentException("A native build plan has no operations.", nameof(plan));
        if (plan.preprocessingOperations.IsDefault) throw new ArgumentException("Native preprocessing operations must be initialized.", nameof(plan));
        // Resolve declared encodings before generating inputs or launching any
        // compiler. Invalid names cannot leave a partially executed plan.
        foreach (NativeBuildCommand command in plan.commands.Concat(plan.dependencyScans.Select(scan => scan.command))
            .Concat(plan.preprocessingOperations.Select(operation => operation.command)).Concat(plan.linkDependencyScans.Select(scan => scan.command)))
            _ = OutputEncoding(command);
        ImmutableArray<NativePreprocessingOperation> proofs = plan.preprocessingOperations.Where(operation => preprocessOnly || operation.preprocessingOnly).ToImmutableArray();
        if (preprocessOnly && proofs.IsEmpty) throw new ArgumentException("Native source view requires at least one exact preprocessing operation.", nameof(plan));
        if (sourceVerifier is not null)
        {
            if (verificationSources.IsDefaultOrEmpty || plan.preprocessingOperations.IsDefault)
                throw new ArgumentException("Independent source fact verification requires explicit sources and initialized preprocessing operations.");
            StringComparer comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            proofs = proofs.Concat(verificationSources.Distinct(comparer).Select(source =>
            {
                NativePreprocessingOperation[] matches = plan.preprocessingOperations.Where(operation => comparer.Equals(Path.GetFullPath(operation.source), Path.GetFullPath(source))).ToArray();
                if (matches.Length != 1) throw new NotSupportedException("The native provider did not supply exactly one effective-source preprocessing operation for " + source);
                return matches[0];
            })).DistinctBy(operation => Path.GetFullPath(operation.source), comparer).ToImmutableArray();
        }
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (File.Exists(plan.outputFile)) throw new InvalidOperationException("A native build cannot reuse a previous scratch output.");
        Directory.CreateDirectory(Path.GetDirectoryName(plan.outputFile)!);
        PrepareInputs(plan, cancellationToken);
        if (!SourceSnapshotVerifier.Matches(plan.argumentInputs, cancellationToken)) throw new IOException("Native response-file argument inputs changed after the plan was frozen.");
        ImmutableArray<ProcessExecutionResult>.Builder results = ImmutableArray.CreateBuilder<ProcessExecutionResult>();
        Stopwatch elapsed = Stopwatch.StartNew();
        ImmutableArray<string> exclusions = (plan.scratchDirectory is string scratch ? ImmutableArray.Create(scratch) : [])
            .AddRange(plan.snapshotExcludedDirectories);
        ImmutableArray<DirectoryInputDependency> search = plan.includeSearchDirectories.Concat(plan.librarySearchDirectories)
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .Select(directory => DirectoryMembershipDigest.Capture(directory, exclusions, cancellationToken)).ToImmutableArray();
        ImmutableArray<InputDependency> external = plan.externalInputFiles.Concat(plan.commands.Select(command => command.executable))
            .Concat(proofs.Select(proof => proof.command.executable)).Concat(proofs.SelectMany(proof => proof.command.inputFiles))
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .Select(path => CaptureFile(path, cancellationToken)).ToImmutableArray();
        if ((plan.compilerFamily ?? plan.providerId) == "gnu")
        {
            NativeBuildCommand compiler = plan.commands[0];
            Directory.CreateDirectory(compiler.workingDirectory);
            TimeSpan probeDeadline = timeout - elapsed.Elapsed;
            if (probeDeadline <= TimeSpan.Zero) throw new TimeoutException("The native build deadline elapsed before compiler identity verification.");
            if (probeDeadline > TimeSpan.FromSeconds(30)) probeDeadline = TimeSpan.FromSeconds(30);
            ProcessExecutionResult identity = await ProcessExecutor.ExecuteAsync(plan.compilerPath ?? compiler.executable, ["-dumpmachine"], compiler.workingDirectory, probeDeadline, cancellationToken).ConfigureAwait(false);
            string triple = identity.standardOutput.Trim();
            string architecture = plan.target.architectureId switch { "x64" => "x86_64", "arm64" => "aarch64", "x86" => "i686", "arm" => "arm", _ => string.Empty };
            bool architectureMatches = triple.StartsWith(architecture + "-", StringComparison.Ordinal) || plan.target.architectureId == "arm" && triple.StartsWith("armv", StringComparison.Ordinal)
                || plan.target.architectureId == "arm64" && triple.StartsWith("arm64-", StringComparison.Ordinal);
            bool abiMatches = plan.target.platformId switch
            {
                "linux" => triple.Contains("linux", StringComparison.Ordinal) && triple.Contains(plan.target.abiId, StringComparison.Ordinal)
                    && (plan.target.abiId != "gnu" || !triple.Contains("musl", StringComparison.Ordinal)),
                "freebsd" => triple.Contains("freebsd", StringComparison.Ordinal),
                "macos" => triple.Contains("apple", StringComparison.Ordinal) && triple.Contains("darwin", StringComparison.Ordinal),
                _ => false
            };
            if (identity.timedOut || identity.exitCode != 0 || !architectureMatches || !abiMatches)
                throw new InvalidOperationException("The GNU driver target does not match the declared native architecture and ABI.");
            results.Add(identity);
        }
        foreach (NativeDependencyScan scan in plan.dependencyScans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TimeSpan remaining = timeout - elapsed.Elapsed;
            if (remaining <= TimeSpan.Zero) throw new TimeoutException("The native build deadline elapsed before dependency discovery.");
            Directory.CreateDirectory(scan.command.workingDirectory);
            ProcessExecutionResult scanResult = await ProcessExecutor.ExecuteAsync(scan.command.executable, scan.command.arguments, scan.command.workingDirectory, remaining,
                cancellationToken, environmentOverrides: scan.command.environmentOverrides, outputEncoding: OutputEncoding(scan.command)).ConfigureAwait(false);
            results.Add(scanResult);
            if (scanResult.timedOut || scanResult.exitCode != 0) throw new InvalidOperationException($"Native dependency operation '{scan.command.id}' failed: {scanResult.standardOutput}{Environment.NewLine}{scanResult.standardError}");
        }
        string dependencyOrigin = plan.scratchDirectory ?? plan.commands[0].workingDirectory;
        ImmutableArray<string> dependencyPaths = BuildDependencyCollector.Read(plan.dependencyScans.Select(scan => scan.dependencyFile), dependencyOrigin, cancellationToken);
        ImmutableArray<InputDependency> dependencies = dependencyPaths.Select(path => CaptureFile(path, cancellationToken)).ToImmutableArray();
        SourceSnapshot snapshot = new(plan.argumentInputs.files.Concat(dependencies).Concat(external).DistinctBy(input => input.path, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToImmutableArray(), search)
        { excludedDirectories = exclusions, absentInputs = plan.argumentInputs.absentInputs };
        if (!SourceSnapshotVerifier.Matches(plan.argumentInputs, cancellationToken)) throw new IOException("Native response-file argument inputs changed during dependency discovery.");
        if (!SourceSnapshotVerifier.Matches(snapshot, cancellationToken)) throw new IOException("Native headers or include candidates changed during dependency discovery.");
        foreach (NativePreprocessingOperation proof in proofs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (plan.scratchDirectory is not string proofScratch || !Within(proof.outputFile, proofScratch) || File.Exists(proof.outputFile))
                throw new IOException("Native preprocessing must create a new file inside the owned scratch directory.");
            Directory.CreateDirectory(Path.GetDirectoryName(proof.outputFile)!);
            TimeSpan remaining = timeout - elapsed.Elapsed;
            if (remaining <= TimeSpan.Zero) throw new TimeoutException("The native deadline elapsed before effective-source fact verification.");
            ProcessExecutionResult preprocessed = await ProcessExecutor.ExecuteAsync(proof.command.executable, proof.command.arguments,
                proof.command.workingDirectory, remaining, cancellationToken, environmentOverrides: proof.command.environmentOverrides,
                outputEncoding: OutputEncoding(proof.command)).ConfigureAwait(false);
            results.Add(preprocessed);
            if (preprocessed.timedOut || preprocessed.exitCode != 0)
                throw new InvalidOperationException("Native effective-source preprocessing failed: " + preprocessed.standardOutput + Environment.NewLine + preprocessed.standardError);
            if (!File.Exists(proof.outputFile)) throw new FileNotFoundException("The native compiler did not produce its declared effective source.", proof.outputFile);
            NativeDependencyFile proofDependencies = proof.dependencyFile ?? throw new NotSupportedException("Independent native preprocessing requires its own actual compiler dependency output.");
            NativeDependencyScan[] proofScans = plan.dependencyScans.Where(scan => string.Equals(Path.GetFullPath(scan.dependencyFile.source), Path.GetFullPath(proof.source), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)).ToArray();
            if (proofScans.Length != 1) throw new InvalidOperationException("A preprocessing input requires exactly one matching independent dependency scan.");
            ImmutableArray<string> expectedProofDependencies = BuildDependencyCollector.Read([proofScans[0].dependencyFile], dependencyOrigin, cancellationToken);
            ImmutableArray<string> actualProofDependencies = BuildDependencyCollector.Read([proofDependencies], dependencyOrigin, cancellationToken);
            if (!expectedProofDependencies.SequenceEqual(actualProofDependencies, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
                throw new IOException("The actual preprocessing dependency closure differs from its independent preflight scan.");
            SourceSnapshot frozenSource = new([CaptureFile(proof.outputFile, cancellationToken), CaptureFile(proofDependencies.path, cancellationToken)], []);
            if (sourceVerifier is not null && verificationSources.Contains(proof.source, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
            {
                if (!SourceSnapshotVerifier.Matches(snapshot, cancellationToken) || !SourceSnapshotVerifier.Matches(plan.argumentInputs, cancellationToken))
                    throw new IOException("Native inputs changed while their effective declarations were preprocessed.");
                TimeSpan verifierRemaining = timeout - elapsed.Elapsed;
                if (verifierRemaining <= TimeSpan.Zero) throw new TimeoutException("The native deadline elapsed before detached effective-source fact verification.");
                using CancellationTokenSource proofDeadline = new(verifierRemaining);
                using CancellationTokenSource proofCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, proofDeadline.Token);
                try { await sourceVerifier(proof, proofCancellation.Token).ConfigureAwait(false); }
                catch (OperationCanceledException failure)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (proofDeadline.IsCancellationRequested) throw new TimeoutException("The native deadline elapsed during detached effective-source fact verification.", failure);
                    throw;
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (proofDeadline.IsCancellationRequested || elapsed.Elapsed >= timeout)
                    throw new TimeoutException("The native deadline elapsed during detached effective-source fact verification.");
            }
            // Without a detached verifier, no external operation separates the
            // two input fences. Keep the later complete check, including the
            // actual preprocessed bytes and dependency output, for that branch.
            if (!SourceSnapshotVerifier.Matches(frozenSource, cancellationToken) || !SourceSnapshotVerifier.Matches(snapshot, cancellationToken)
                || !SourceSnapshotVerifier.Matches(plan.argumentInputs, cancellationToken))
                throw new IOException("Native effective source or compiler inputs changed during detached fact verification.");
        }
        if (preprocessOnly)
        {
            // A preprocessing-only plan requires nonempty proofs. Its last
            // complete post-proof fence has just checked the same inputs; only
            // loop completion and this immutable result construction intervene.
            cancellationToken.ThrowIfCancellationRequested();
            if (elapsed.Elapsed >= timeout) throw new TimeoutException("The native deadline elapsed before source view extraction completed.");
            return new(results.ToImmutable(), snapshot) { argumentInputs = plan.argumentInputs };
        }
        foreach (NativeBuildCommand command in plan.commands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ImmutableArray<string> linkDependencies = [];
            if (command.linkDependencyTrace is NativeLinkTraceFormat traceFormat)
            {
                NativeLinkDependencyScan[] probes = plan.linkDependencyScans.Where(scan => scan.finalCommandId == command.id).ToArray();
                if (probes.Length != 1 || probes[0].format != traceFormat) throw new InvalidOperationException("A final linker trace requires exactly one matching independent link probe.");
                NativeBuildCommand probe = probes[0].command;
                TimeSpan probeDeadline = timeout - elapsed.Elapsed;
                if (probeDeadline <= TimeSpan.Zero) throw new TimeoutException("The native build deadline elapsed before linker dependency discovery.");
                Directory.CreateDirectory(probe.workingDirectory);
                ProcessExecutionResult probeResult = await ProcessExecutor.ExecuteAsync(probe.executable, probe.arguments, probe.workingDirectory, probeDeadline, cancellationToken,
                    environmentOverrides: probe.environmentOverrides, outputEncoding: OutputEncoding(probe)).ConfigureAwait(false);
                results.Add(probeResult);
                if (probeResult.timedOut || probeResult.exitCode != 0) throw new InvalidOperationException($"Native linker dependency operation '{probe.id}' failed: {probeResult.standardOutput}{Environment.NewLine}{probeResult.standardError}");
                foreach (string output in probe.outputFiles)
                    if (!File.Exists(output)) throw new FileNotFoundException("The linker probe did not produce its declared candidate output.", output);
                linkDependencies = BuildDependencyCollector.ReadLinkTrace(probeResult.standardOutput + "\n" + probeResult.standardError, traceFormat, probe.workingDirectory);
                snapshot = snapshot with
                {
                    files = snapshot.files.Concat(linkDependencies.Select(path => CaptureFile(path, cancellationToken)))
                    .DistinctBy(input => input.path, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToImmutableArray()
                };
                if (!SourceSnapshotVerifier.Matches(snapshot, cancellationToken)) throw new IOException("Native compiler or linker inputs changed before final linking.");
            }
            TimeSpan remaining = timeout - elapsed.Elapsed;
            if (remaining <= TimeSpan.Zero) throw new TimeoutException("The native build deadline elapsed before the next operation.");
            Directory.CreateDirectory(command.workingDirectory);
            ProcessExecutionResult result = await ProcessExecutor.ExecuteAsync(command.executable, command.arguments, command.workingDirectory, remaining, cancellationToken,
                environmentOverrides: command.environmentOverrides, outputEncoding: OutputEncoding(command)).ConfigureAwait(false);
            results.Add(result);
            if (result.timedOut || result.exitCode != 0) throw new InvalidOperationException($"Native operation '{command.id}' failed: {result.standardOutput}{Environment.NewLine}{result.standardError}");
            foreach (string output in command.outputFiles)
                if (!File.Exists(output)) throw new FileNotFoundException($"Native operation '{command.id}' did not produce its declared output.", output);
            if (command.linkDependencyTrace is NativeLinkTraceFormat finalTrace)
            {
                ImmutableArray<string> finalInputs = BuildDependencyCollector.ReadLinkTrace(result.standardOutput + "\n" + result.standardError, finalTrace, command.workingDirectory);
                if (!linkDependencies.SequenceEqual(finalInputs, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
                    throw new IOException("The actual linker input closure differs from its independent probe.");
            }
        }
        if (!File.Exists(plan.outputFile)) throw new FileNotFoundException("Native tools succeeded without producing the declared artifact.", plan.outputFile);
        NativeBinaryIdentity.Validate(plan.outputFile, plan.target, plan.outputKind);
        ImmutableArray<string> compiledDependencies = BuildDependencyCollector.Read(plan.dependencyFiles.Concat(proofs.Select(proof => proof.dependencyFile!)), dependencyOrigin, cancellationToken);
        if (!dependencyPaths.SequenceEqual(compiledDependencies, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
            throw new IOException("The actual compiler dependency closure differs from the preflight dependency snapshot.");
        if (!SourceSnapshotVerifier.Matches(snapshot, cancellationToken)) throw new IOException("Native input bytes or include candidates changed during construction.");
        if (!SourceSnapshotVerifier.Matches(plan.argumentInputs, cancellationToken)) throw new IOException("Native response-file argument inputs changed during construction.");
        return new(results.ToImmutable(), snapshot) { argumentInputs = plan.argumentInputs };
    }

    private static Encoding? OutputEncoding(NativeBuildCommand command)
        => command.outputEncoding is string name ? Encoding.GetEncoding(name) : null;

    private static InputDependency CaptureFile(
        string path,
        CancellationToken cancellationToken
    ) {
        cancellationToken.ThrowIfCancellationRequested();
        using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.SequentialScan);
        return new(path, PhysicalPathResolver.Resolve(path), file.Length, new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(file))));
    }

    private static bool Within(
        string path,
        string root
    ) {
        string physical = PhysicalPathResolver.Resolve(path), parent = PhysicalPathResolver.Resolve(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return physical.StartsWith(parent + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static void PrepareInputs(
        NativeBuildPlan plan,
        CancellationToken cancellationToken
    ) {
        if (plan.generatedInputFiles.IsDefault) throw new ArgumentException("Native generated inputs must be initialized.", nameof(plan));
        if (plan.generatedInputFiles.IsEmpty) return;
        if (plan.scratchDirectory is not string scratch || !Path.IsPathFullyQualified(scratch))
            throw new ArgumentException("A generated native build plan requires an absolute owned scratch directory.", nameof(plan));
        string root = PhysicalPathResolver.Resolve(scratch);
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        HashSet<string> seen = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        List<(string path, string content)> writes = [];
        foreach (NativeBuildInput input in plan.generatedInputFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(input.relativePath) || input.relativePath.Contains('\\') || input.relativePath.Contains(':') || Path.IsPathRooted(input.relativePath)
                || input.relativePath.Split('/').Any(component => component is "" or "." or ".."))
                throw new ArgumentException("Generated native input paths must be portable paths within scratch.", nameof(plan));
            string path = Path.GetFullPath(input.relativePath, scratch);
            string physical = PhysicalPathResolver.Resolve(path);
            if (!physical.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison))
                throw new IOException("A generated native build input escapes the owned scratch directory.");
            if (!seen.Add(physical) || File.Exists(path) || Directory.Exists(path))
                throw new IOException("A native build cannot overwrite or duplicate provider input files.");
            writes.Add((path, input.content ?? throw new ArgumentException("Native input contents cannot be null.", nameof(plan))));
        }
        foreach ((string path, string content) in writes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using FileStream output = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            byte[] bytes = Encoding.UTF8.GetBytes(content);
            output.Write(bytes);
        }
    }
}
