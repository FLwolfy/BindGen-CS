using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Composition;
using BGCS.Configuration;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.Infrastructure.IO;
using BGCS.Infrastructure.Execution;
using BGCS.NativeBuild.Execution;
using BGCS.NativeBuild.Planning;
using BGCS.NativeBuild.Validation;

namespace BGCS;

internal sealed class NativeCompilerProofValidator
{
    internal sealed record Result(
        ImmutableArray<SourceSnapshot> inputs,
        object evidence,
        string privateRoot
    );

    internal async ValueTask<Result> VerifyAsync(
        BindingConfiguration configuration,
        NativeTargetDescriptor target,
        FrontendRequest originalSource,
        IEnumerable<string> sources,
        IEnumerable<string> includeDirectories,
        ImmutableArray<string> excludedDirectories,
        GenerationInputTracker inputTracker,
        NativeEffectiveFactVerifier factVerifier,
        string factsInput,
        CancellationToken cancellationToken
    ) {
        if (target.toolchain.compilerPath is null)
            throw new NotSupportedException("Parser-adapted native facts require the selected compiler for independent compilation.");
        StringComparer paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        if (originalSource.language != SourceLanguage.Cpp || originalSource.target.targetId != target.targetId
            || originalSource.target.triple != target.triple || originalSource.target.platformId != target.platformId
            || originalSource.target.architectureId != target.architectureId || originalSource.target.abiId != target.abiId
            || originalSource.target.byteOrder != target.byteOrder
            || !paths.Equals(originalSource.target.toolchain.compilerPath, target.toolchain.compilerPath)
            || !Path.IsPathFullyQualified(originalSource.pathBase))
            throw new InvalidOperationException("The independent native proof requires its original prepared source and selected compiler identity.");
        NativeToolchainDescriptor originalToolchain = originalSource.target.toolchain;
        // BindingHost has already expanded target and source arguments, in order,
        // into originalSource.arguments. Never append the descriptor arguments a
        // second time, or replace this identity with final native-build flags.
        NativeToolchainDescriptor proofToolchain = new(originalToolchain.compilerPath, originalToolchain.sysRoot,
            originalToolchain.systemIncludeFolders, originalToolchain.defines, [], originalToolchain.cxxSystemIncludeFolders,
            originalToolchain.sdkVersion, originalToolchain.compilerVersion, originalToolchain.linkerPath, originalToolchain.libraryDirectories);
        NativeTargetDescriptor proofTarget = new(target.targetId, target.platformId, target.architectureId, target.abiId,
            target.triple, proofToolchain, target.capabilities, target.byteOrder);
        ImmutableArray<string> originalExclusions = excludedDirectories.AddRange(originalSource.snapshotExcludedDirectories).Distinct(paths).ToImmutableArray();
        string root = Path.Combine(PhysicalPathResolver.Resolve(Path.GetTempPath()), "bgcs-native-fact-proof-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        ImmutableArray<SourceSnapshot>.Builder inputs = ImmutableArray.CreateBuilder<SourceSnapshot>();
        List<object> executions = [];
        Dictionary<string, string> literalRoles = new(paths);
        bool objectLinkPerformed = false;
        INativeBuildProvider? provider = null;
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(configuration.execution.nativeBuildTimeoutSeconds));
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            string[] frozenSources = sources.Select(Path.GetFullPath).Distinct(paths).ToArray();
            if (frozenSources.Length == 0)
                throw new InvalidOperationException("Parser-adapted facts omitted their mandatory independent native proof sources.");
            try
            {
                provider = new NativeBuildProviderResolver().Resolve(proofTarget, "auto");
                if (provider.id is not ("gnu" or "clang" or "clang-cl" or "msvc"))
                    throw new NotSupportedException("No matching direct native compiler provider was resolved.");
            }
            catch (Exception failure) when (failure is NotSupportedException or InvalidOperationException)
            {
                throw new NotSupportedException("Independent parser-adapted fact proof requires the selected native compiler driver. " + failure.Message, failure);
            }
            inputTracker.Capture(provider, target, linked.Token);
            ImmutableArray<string> compilerArguments = SourceArguments(originalSource, provider.id);
            ImmutableArray<string> defines = originalSource.defines.AddRange(originalToolchain.defines.Where(define => !define.StartsWith("_MSC_VER=", StringComparison.Ordinal)));
            for (int index = 0; index < frozenSources.Length; index++)
            {
                linked.Token.ThrowIfCancellationRequested();
                string source = frozenSources[index];
                if (Path.GetExtension(source) is not (".cpp" or ".cc" or ".cxx"))
                    throw new NotSupportedException("The declaration-prelude proof currently requires C++ compilation units.");
                string scratch = Path.Combine(root, index.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Directory.CreateDirectory(scratch);
                NativeBuildRequest request = new(proofTarget,
                    [new(source, SourceLanguage.Cpp, originalSource.languageStandard)], scratch, Path.Combine(scratch, "proof.o"))
                {
                    argumentWorkingDirectory = originalSource.pathBase,
                    outputKind = NativeOutputKind.Object,
                    preprocessingSources = index == 0 ? [new(factsInput, SourceLanguage.Cpp, originalSource.languageStandard)] : [],
                    snapshotExcludedDirectories = originalExclusions.Add(root),
                    includeDirectories = originalSource.includeDirectories
                        .Concat(includeDirectories).Distinct(paths).ToImmutableArray(),
                    systemIncludeDirectories = originalSource.systemIncludeDirectories
                        .Concat(originalToolchain.systemIncludeFolders).Concat(originalToolchain.cxxSystemIncludeFolders).Distinct(paths).ToImmutableArray(),
                    defines = defines,
                    compilerArguments = compilerArguments
                };
                // Providers capture argument inputs from the request, while the
                // executor's complete search membership uses the plan's scope.
                // Both must retain the same private-output exclusions.
                NativeBuildPlan plan = provider.Plan(request, linked.Token) with
                { snapshotExcludedDirectories = request.snapshotExcludedDirectories };
                if (plan.providerId != provider.id || (plan.compilerFamily ?? plan.providerId) != provider.id || plan.outputKind != NativeOutputKind.Object
                    || !paths.Equals(plan.outputFile, request.outputFile) || plan.target != proofTarget)
                    throw new InvalidOperationException("The independent native proof changed its selected compiler, target or object-only contract.");
                NativeBuildExecutionResult result;
                try
                {
                    result = await new PlannedNativeBuildExecutor().ExecuteWithDependenciesAsync(
                        plan, TimeSpan.FromSeconds(configuration.execution.nativeBuildTimeoutSeconds), linked.Token,
                        index == 0 ? (
                            operation,
                            token
                        ) => factVerifier.VerifyAsync(plan, operation, token, root) : null,
                        index == 0 ? [factsInput] : []).ConfigureAwait(false);
                }
                catch (InvalidOperationException failure)
                {
                    throw new InvalidOperationException("Independent parser-adapted native fact compilation failed. " + failure.Message, failure);
                }
                if (!File.Exists(request.outputFile))
                    throw new IOException("The selected compiler did not produce the required independent native proof object.");
                inputs.Add(Retain(result.inputSnapshot));
                inputs.Add(Retain(result.argumentInputs));
                foreach (NativeStringProofReceipt literal in factVerifier.literalProofs)
                    foreach (string input in literal.command.inputFiles.Where(Private))
                        literalRoles[input] = LogicalPath(Path.Combine(Path.GetDirectoryName(input)!, "literal-proof.cpp"));
                objectLinkPerformed |= plan.commands.Any(command => command.id == "link" || command.linkDependencyTrace is not null);
                executions.Add(new
                {
                    commands = plan.commands.Select(Command).ToImmutableArray(),
                    dependencyScans = plan.dependencyScans.Select(scan => Command(scan.command)).ToImmutableArray(),
                    preprocessingOperations = plan.preprocessingOperations.Select(operation => new
                    { command = Command(operation.command), operation.preprocessingOnly, source = Normalize(operation.source), outputFile = Normalize(operation.outputFile) }).ToImmutableArray(),
                    linkDependencyScans = plan.linkDependencyScans.Select(scan => new { scan.finalCommandId, scan.format, command = Command(scan.command) }).ToImmutableArray(),
                    results = result.executions.Select(Execution).ToImmutableArray()
                });
            }
            foreach (NativeStringProofReceipt literal in factVerifier.literalProofs)
                inputs.Add(Retain(literal.argumentInputs));
            cancellationToken.ThrowIfCancellationRequested();
            if (SourceSnapshotBatch.GetMismatch(inputs, linked.Token) is string mismatch)
                throw new IOException("Independent native fact proof inputs changed before acceptance. " + mismatch);
            return new(inputs.ToImmutable(), new
            {
                schemaVersion = 2,
                compilerFamily = provider.id,
                compiled = true,
                finalLinked = false,
                objectLinkPerformed,
                invoked = false,
                scope = "selected canonical identities, constants, storage assertions and emitted native bridge compilation",
                sourceIdentity = new { originalSource.languageStandard, arguments = compilerArguments.Select(Normalize).ToImmutableArray(), defines },
                literalProofs = factVerifier.literalProofs.Select(literal => new
                { command = Command(literal.command), literal.source, literal.sourceDigest, literal.verifiedConstants, execution = Execution(literal.execution) }).ToImmutableArray(),
                executions
            }, root);
        }
        catch (OperationCanceledException failure)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (deadline.IsCancellationRequested)
                throw new TimeoutException("Independent selected-native fact compilation exceeded its shared deadline.", failure);
            throw;
        }
        finally
        {
            if (provider is IAsyncDisposable asynchronous) await asynchronous.DisposeAsync().ConfigureAwait(false);
            else if (provider is IDisposable disposable) disposable.Dispose();
            if (Directory.Exists(root))
            {
                if (!string.Equals(PhysicalPathResolver.Resolve(root), root,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    throw new IOException("The private native proof directory was retargeted before cleanup.");
                Directory.Delete(root, recursive: true);
            }
        }

        // Membership digests are meaningful only with their exact capture-time
        // exclusion list. Keep it, including the subsequently deleted scratch
        // root, while removing only dependencies owned by that private root.
        SourceSnapshot Retain(SourceSnapshot snapshot) => snapshot with
        {
            files = snapshot.files.Where(input => !Private(input.path)).ToImmutableArray(),
            searchDirectories = snapshot.searchDirectories.Where(input => !Private(input.path)).ToImmutableArray(),
            absentInputs = snapshot.absentInputs.Where(input => !Private(input.path)).ToImmutableArray()
        };
        bool Private(string path)
        {
            string absolute = PhysicalPathResolver.Resolve(path), parent = PhysicalPathResolver.Resolve(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return absolute.Equals(parent, comparison) || absolute.StartsWith(parent + Path.DirectorySeparatorChar, comparison);
        }
        string LogicalPath(string path) => "native-proof:/" + Path.GetRelativePath(root, path).Replace('\\', '/');
        string Normalize(string value)
        {
            StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            foreach (var literal in literalRoles)
                value = value.Replace(literal.Key, literal.Value, comparison)
                    .Replace(literal.Key.Replace('\\', '/'), literal.Value, comparison)
                    .Replace(Path.GetFileName(literal.Key), "literal-proof.cpp", comparison);
            foreach (string prefix in new[] { root, root.Replace('\\', '/') }.Distinct(paths))
            {
                if (value.Equals(prefix, comparison)) return "native-proof:/";
                if (value.StartsWith(prefix + Path.DirectorySeparatorChar, comparison) || value.StartsWith(prefix + "/", comparison))
                    return "native-proof:/" + value[(prefix.Length + 1)..].Replace('\\', '/');
                value = value.Replace(prefix + "\\", "native-proof:/", comparison).Replace(prefix + "/", "native-proof:/", comparison);
            }
            return value;
        }
        object Command(NativeBuildCommand command) => new
        {
            command.id,
            executable = Normalize(command.executable),
            arguments = command.arguments.Select(Normalize).ToImmutableArray(),
            workingDirectory = Normalize(command.workingDirectory),
            inputFiles = command.inputFiles.Select(Normalize).ToImmutableArray(),
            outputFiles = command.outputFiles.Select(Normalize).ToImmutableArray(),
            command.linkDependencyTrace,
            command.outputEncoding,
            environmentOverrides = new SortedDictionary<string, string?>(command.environmentOverrides.ToDictionary(item => item.Key,
                item => item.Value is null ? null : Normalize(item.Value), StringComparer.Ordinal), StringComparer.Ordinal)
        };
        object Execution(ProcessExecutionResult result) => new
        { result.exitCode, result.timedOut, standardOutput = Normalize(result.standardOutput), standardError = Normalize(result.standardError) };
    }

    private static ImmutableArray<string> SourceArguments(
        FrontendRequest source,
        string family
    ) {
        ImmutableArray<string>.Builder result = ImmutableArray.CreateBuilder<string>();
        if (family != "msvc")
        {
            foreach (string argument in source.arguments)
            {
                if (argument == "-fparse-all-comments") continue;
                result.Add(family == "clang-cl" ? "/clang:" + argument : argument);
            }
            return result.ToImmutable();
        }
        if (!Version.TryParse(source.target.toolchain.compilerVersion, out Version? compiler))
            throw new NotSupportedException("The prepared MSVC source proof omitted its exact compiler version.");
        string compatibility = "-fms-compatibility-version=" + compiler.Major.ToString(System.Globalization.CultureInfo.InvariantCulture) + "." + compiler.Minor.ToString(System.Globalization.CultureInfo.InvariantCulture);
        for (int index = 0; index < source.arguments.Length; index++)
        {
            string argument = source.arguments[index];
            if (argument.StartsWith("-Wp,", StringComparison.Ordinal) || argument.StartsWith("-Wl,", StringComparison.Ordinal) || argument.StartsWith("-Wa,", StringComparison.Ordinal))
                throw new NotSupportedException("The selected MSVC source proof has no faithful mapping for forwarded driver options: " + argument);
            if (argument == compatibility || argument is "-fms-compatibility" or "-fms-extensions" or "-fparse-all-comments" or "-w" || argument.StartsWith("-W", StringComparison.Ordinal)) continue;
            if (argument is "-D" or "-U" or "-I" or "-include" or "-isystem")
            {
                if (++index >= source.arguments.Length) throw new ArgumentException("A prepared native source option omitted its value: " + argument);
                result.Add((argument == "-include" ? "/FI" : argument == "-isystem" ? "/external:I" : "/" + argument[1..]) + source.arguments[index]);
            }
            else if (argument.StartsWith("-D", StringComparison.Ordinal) || argument.StartsWith("-U", StringComparison.Ordinal) || argument.StartsWith("-I", StringComparison.Ordinal)) result.Add("/" + argument[1..]);
            else if (argument == "-funsigned-char") result.Add("/J");
            else if (argument.StartsWith("-fpack-struct=", StringComparison.Ordinal)) result.Add("/Zp" + argument["-fpack-struct=".Length..]);
            else throw new NotSupportedException("The selected MSVC source proof has no faithful mapping for parser option: " + argument);
        }
        return result.ToImmutable();
    }
}
