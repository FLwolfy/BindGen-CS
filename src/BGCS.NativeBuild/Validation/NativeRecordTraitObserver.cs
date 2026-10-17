using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.Contracts.Requests;
using BGCS.Infrastructure.Execution;
using BGCS.Infrastructure.IO;
using BGCS.NativeBuild.Execution;
using BGCS.NativeBuild.Planning;

namespace BGCS.NativeBuild.Validation;

/// <summary>Acquires selected-native constexpr record traits through exact target-object symbol sizes, without executing target code.</summary>
public sealed class NativeRecordTraitObserver
{
    private const int C_RECIPE_REVISION = 3;
    private const string C_SYMBOL_PREFIX = "bgcs_native_record_trait_";
    private const string C_TYPE_PATTERN = @"\A(?:::)?[A-Za-z_][A-Za-z_0-9 :<>,*&]*\z";

    /// <summary>Compiles the already-completed native source tokens and independently inspects one-byte or two-byte trait symbols.</summary>
    /// <param name="plan">The actual selected-driver plan whose preprocessing operation has completed.</param>
    /// <param name="operation">The exact completed C++ operation, including its still-live raw output file.</param>
    /// <param name="preprocessedDigest">The raw output digest captured before parser or extension execution.</param>
    /// <param name="module">The frozen graph containing the complete, accessible record declarations to query.</param>
    /// <param name="workingDirectory">A new absolute caller-selected private query directory; it is removed before return.</param>
    /// <param name="timeout">One deadline for argument capture, object compilation, inspection and verification.</param>
    /// <param name="cancellationToken">Cancels native children and input verification.</param>
    /// <param name="symbolToolPath">An optional exact existing inspector path; otherwise llvm-nm or nm is resolved explicitly.</param>
    /// <param name="sourcePathMappings">Authenticated exact file origins or trailing-separator directory prefixes used only to stabilize the query identity.</param>
    /// <returns>Native traits, retained external input obligations and execution evidence; no linked or invoked target artifact is claimed.</returns>
    /// <exception cref="NotSupportedException">The compiler, source shape, symbol format or object representation cannot prove the query.</exception>
    /// <exception cref="InvalidOperationException">A native operation fails or the completed operation and model disagree.</exception>
    /// <exception cref="IOException">An input, physical owner or object changes, or the inspector omits or corrupts a required symbol.</exception>
    /// <exception cref="ArgumentException">The required operation, target, scratch or timeout contract is invalid.</exception>
    /// <exception cref="TimeoutException">The shared native observation deadline expires.</exception>
    /// <remarks>Only validated ELF object data-symbol sizes are supported; COFF, Mach-O and LTO are explicit failures. Only queried trait fields may be overlaid. Type identity, sizes, alignments, fields and all independent native assertions remain required.</remarks>
    public async ValueTask<NativeRecordTraitObservation> ObserveAsync(
        NativeBuildPlan plan,
        NativePreprocessingOperation operation,
        ContentDigest preprocessedDigest,
        NativeModule module,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken = default,
        string? symbolToolPath = null,
        ImmutableDictionary<string, string>? sourcePathMappings = null
    ) {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(module);
        cancellationToken.ThrowIfCancellationRequested();
        string family = plan.compilerFamily ?? plan.providerId;
        if (family is not ("gnu" or "clang") || operation.language != SourceLanguage.Cpp || module.language != SourceLanguage.Cpp)
            throw new NotSupportedException("Native record trait acquisition requires an exact GNU or Clang C++ operation.");
        if (module.targetId != plan.target.targetId.value || module.triple != plan.target.triple
            || !plan.preprocessingOperations.Contains(operation) || plan.compilerPath != operation.command.executable
            || !Path.IsPathFullyQualified(operation.outputFile) || !File.Exists(operation.outputFile)
            || plan.scratchDirectory is null || !Path.IsPathFullyQualified(plan.scratchDirectory)
            || !Path.IsPathFullyQualified(workingDirectory) || Directory.Exists(workingDirectory) || File.Exists(workingDirectory)
            || timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentException("Native trait acquisition requires the exact completed operation, matching graph and a new absolute private directory.");
        Dictionary<StableId, NativeDeclaration> declarations = module.declarations.ToDictionary(declaration => declaration.id);
        NativeDeclaration[] candidates = module.declarations.Where(declaration => declaration.kind == "Class"
            && !declaration.isSystemDeclaration && declaration.isCompleteDefinition && Accessible(declaration, [])
            && (declaration.templateKind is null or "NormalClass" or "TemplateSpecializedClass")
            && declaration.templateParameters.IsEmpty && Named(declaration)
            && (declaration.isDefaultConstructible is not null || declaration.isStandardLayout is not null || declaration.isTriviallyCopyable is not null)).ToArray();
        if (candidates.Length == 0)
            return new([], [], Digest(JsonSerializer.SerializeToUtf8Bytes(new { recipeRevision = C_RECIPE_REVISION, facts = Array.Empty<NativeRecordTraitFact>() })), null);
        if (new DirectoryInfo(workingDirectory).LinkTarget is not null) throw new IOException("The native record trait scratch leaf cannot be a link.");
        string root = PhysicalPathResolver.Resolve(Path.GetFullPath(workingDirectory));
        string physical = root;
        Directory.CreateDirectory(root);
        List<SourceSnapshot> snapshots = [];
        NativeRecordTraitObservation? observation = null;
        Stopwatch elapsed = Stopwatch.StartNew();
        using CancellationTokenSource deadline = new(timeout);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        CancellationToken token = linked.Token;
        string environment = EnvironmentDigest();
        Exception? originalFailure = null;
        try
        {
            VerifyOwner();
            SourceSnapshot raw = Capture([operation.outputFile]);
            if (raw.files.Single().digest != preprocessedDigest)
                throw new IOException("Completed native preprocessing bytes changed before native trait acquisition.");
            NativeArgumentExpansionResult expanded = new NativeArgumentInputSnapshotBuilder().Expand(operation.command.arguments, operation.command.workingDirectory,
                windowsResponseSyntax: false, clangConfigurations: family == "clang", compilerPath: operation.command.executable,
                cancellationToken: token, snapshotExcludedDirectories: plan.snapshotExcludedDirectories.Add(root),
                includeSearchDirectories: plan.includeSearchDirectories, sysRoot: plan.target.toolchain.sysRoot);
            snapshots.Add(raw);
            snapshots.Add(plan.argumentInputs);
            snapshots.Add(expanded.snapshot);
            byte[] rawBytes = await File.ReadAllBytesAsync(operation.outputFile, token).ConfigureAwait(false);
            if (Digest(rawBytes) != preprocessedDigest)
                throw new IOException("Completed native preprocessing bytes changed before native trait acquisition.");
            int bom = rawBytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
            string original = new UTF8Encoding(false, true).GetString(rawBytes, bom, rawBytes.Length - bom);
            var renderer = new NativeCallableAssertionEmitter(module);
            Dictionary<string, (StableId identity, int trait)> expected = new(StringComparer.Ordinal);
            StringBuilder suffix = new("\nextern \"C\" {\n");
            HashSet<string> guards = new(StringComparer.Ordinal) { "extern", "unsigned", "char", "static_assert", "__attribute__", "used", "__is_class", "__is_union", "__is_constructible", "__is_standard_layout", "__is_trivially_copyable" };
            foreach (NativeDeclaration declaration in candidates)
            {
                if (declaration.type is not StableId identity || module.GetType(identity).category != NativeTypeCategory.Record)
                    throw new InvalidOperationException("A queried native record omitted its exact record type identity: " + declaration.qualifiedName);
                string name = renderer.RenderStorageType(identity, string.Empty);
                if (!Regex.IsMatch(name, C_TYPE_PATTERN, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                    throw new NotSupportedException("A native record trait query has no controlled concrete type spelling: " + declaration.qualifiedName);
                suffix.Append("static_assert(__is_class(").Append(name).Append(") || __is_union(").Append(name).AppendLine("), \"BGCS native trait query requires a record identity\");");
                foreach (Match identifier in Regex.Matches(name, @"[A-Za-z_][A-Za-z_0-9]*", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) guards.Add(identifier.Value);
                string stem = C_SYMBOL_PREFIX + Digest(Encoding.UTF8.GetBytes(declaration.id.value)).value;
                string[] builtins = ["__is_constructible", "__is_standard_layout", "__is_trivially_copyable"];
                for (int trait = 0; trait < builtins.Length; trait++)
                {
                    string symbol = stem + "_" + trait.ToString(CultureInfo.InvariantCulture);
                    expected.Add(symbol, (declaration.id, trait));
                    guards.Add(symbol);
                    suffix.Append("__attribute__((used)) unsigned char ").Append(symbol).Append('[').Append(builtins[trait]).Append('(').Append(name).AppendLine(") ? 2 : 1] = {};");
                }
            }
            suffix.AppendLine("}");
            // The source tokens have already been expanded by the selected native
            // driver. Remove only authenticated macro directives, preserving every
            // token, pragma and line marker; never preprocess those tokens again.
            char[] frozen = original.ToCharArray();
            HashSet<string> active = new(StringComparer.Ordinal);
            foreach (var directive in NativeMacroDirectiveReader.ReadDirectives(original))
            {
                if (directive.operation is not ("define" or "undef")) continue;
                if (directive.operation == "define") active.Add(directive.name); else active.Remove(directive.name);
                for (int index = directive.start; index < directive.start + directive.length; index++)
                    if (frozen[index] is not ('\r' or '\n')) frozen[index] = ' ';
            }
            if (active.Overlaps(guards)) throw new NotSupportedException("Native macros shadow a required record-trait proof token.");
            string source = new string(frozen) + suffix;
            string input = Path.Combine(root, "native-record-traits.ii"), output = Path.Combine(root, "native-record-traits.o");
            await File.WriteAllTextAsync(input, source, new UTF8Encoding(false), token).ConfigureAwait(false);
            SourceSnapshot recipe = Capture([input]);
            if (recipe.files.Single().digest != Digest(Encoding.UTF8.GetBytes(source)))
                throw new IOException("The generated native record trait query changed before compilation.");
            snapshots.Add(recipe);
            ImmutableArray<string> arguments = CompileArguments(expanded.arguments, operation, input, output);
            NativeBuildCommand command = operation.command with { id = "native-record-traits", arguments = arguments, inputFiles = [input], outputFiles = [output] };
            ImmutableArray<string> dependencies = operation.dependencyFile is { } dependency
                ? BuildDependencyCollector.Read([dependency], operation.command.workingDirectory, token).ToImmutableArray()
                : throw new InvalidOperationException("Native record trait acquisition requires the completed compiler dependency file.");
            SourceSnapshot external = Capture(plan.externalInputFiles.Concat(dependencies).Append(operation.command.executable).Distinct(Paths).ToImmutableArray());
            snapshots.Add(external);
            NativeBuildPlan query = plan with
            {
                commands = [command],
                outputFile = output,
                outputKind = NativeOutputKind.Object,
                scratchDirectory = root,
                generatedInputFiles = [],
                dependencyScans = [],
                dependencyFiles = [],
                preprocessingOperations = [],
                linkDependencyScans = [],
                snapshotExcludedDirectories = plan.snapshotExcludedDirectories.Add(root),
                argumentInputs = expanded.snapshot,
                externalInputFiles = external.files.Select(file => file.path).Append(input).ToImmutableArray()
            };
            Verify();
            NativeBuildExecutionResult execution = await new PlannedNativeBuildExecutor().ExecuteWithDependenciesAsync(query, Remaining(), token).ConfigureAwait(false);
            NativeBinaryIdentity.Validate(output, plan.target, NativeOutputKind.Object);
            using (FileStream objectFile = File.OpenRead(output))
            {
                byte[] magic = new byte[4];
                objectFile.ReadExactly(magic);
                if (!magic.AsSpan().SequenceEqual(new byte[] { 0x7f, (byte)'E', (byte)'L', (byte)'F' }))
                    throw new NotSupportedException("Native record trait acquisition requires ELF data-symbol sizes; this object format cannot establish them.");
            }
            SourceSnapshot compiled = Capture([output]);
            snapshots.Add(compiled);
            string inspector = symbolToolPath is null
                ? NativeBuildToolDiscovery.FindExecutable("llvm-nm", OperatingSystem.IsWindows() ? "C:/Program Files/LLVM/bin/llvm-nm.exe" : null)
                    ?? NativeBuildToolDiscovery.FindExecutable("nm") ?? throw new FileNotFoundException("Native trait acquisition requires an explicitly resolved llvm-nm or nm.")
                : Path.IsPathFullyQualified(symbolToolPath) && File.Exists(symbolToolPath) ? Path.GetFullPath(symbolToolPath)
                    : throw new FileNotFoundException("The declared native trait symbol tool must exist at its exact absolute path.", symbolToolPath);
            snapshots.Add(Capture([inspector]));
            NativeBuildCommand inspectionCommand = new("native-record-trait-symbols", inspector,
                ["--format=posix", "--print-size", "--defined-only", "--extern-only", output], root)
            { inputFiles = [output], outputEncoding = "utf-8", environmentOverrides = NativeBuildEnvironment.cleanCompiler.SetItem("LC_ALL", "C") };
            Verify();
            ProcessExecutionResult inspection = await ProcessExecutor.ExecuteAsync(inspector, inspectionCommand.arguments, root, Remaining(), token,
                environmentOverrides: inspectionCommand.environmentOverrides, outputEncoding: Encoding.UTF8).ConfigureAwait(false);
            if (inspection.timedOut || inspection.exitCode != 0)
                throw new InvalidOperationException("Native record trait symbol inspection failed: " + inspection.standardOutput + inspection.standardError);
            Verify();
            snapshots.Add(execution.inputSnapshot);
            snapshots.Add(execution.argumentInputs);
            Dictionary<string, bool> values = ReadSymbols(inspection.standardOutput, expected.Keys.ToHashSet(StringComparer.Ordinal), plan.target.platformId == "macos" || plan.target.architectureId == "x86");
            ImmutableArray<NativeRecordTraitFact> facts = candidates.Select(declaration =>
            {
                string stem = C_SYMBOL_PREFIX + Digest(Encoding.UTF8.GetBytes(declaration.id.value)).value;
                return new NativeRecordTraitFact(declaration.id, values[stem + "_0"], values[stem + "_1"], values[stem + "_2"]);
            }).ToImmutableArray();
            NativeRecordTraitEvidence evidence = new(C_RECIPE_REVISION, source, recipe.files.Single().digest, compiled.files.Single().digest,
                query, execution, inspectionCommand, inspection);
            var fingerprint = new
            {
                recipeRevision = C_RECIPE_REVISION,
                query = suffix.ToString(),
                facts,
                compiler = new
                {
                    command.id,
                    executable = Normalize(command.executable),
                    arguments = command.arguments.Select(Normalize),
                    workingDirectory = Normalize(command.workingDirectory),
                    environmentOverrides = command.environmentOverrides.OrderBy(pair => pair.Key, StringComparer.Ordinal),
                    command.outputEncoding
                },
                inspector = new
                {
                    inspectionCommand.id,
                    executable = Normalize(inspector),
                    arguments = inspectionCommand.arguments.Select(Normalize),
                    workingDirectory = Normalize(root),
                    environmentOverrides = inspectionCommand.environmentOverrides.OrderBy(pair => pair.Key, StringComparer.Ordinal),
                    inspectionCommand.outputEncoding
                },
                environment,
                files = snapshots.SelectMany(snapshot => snapshot.files).Where(file => !Private(file.path))
                    .Distinct().OrderBy(file => file.path, StringComparer.Ordinal)
                    .Select(file => new { path = Normalize(file.path), resolvedPath = Normalize(file.resolvedPath), file.length, file.digest })
            };
            observation = new(facts, [], Digest(JsonSerializer.SerializeToUtf8Bytes(fingerprint)), evidence);
            Verify();
        }
        catch (Exception failure)
        {
            originalFailure = failure;
            throw;
        }
        finally
        {
            try
            {
                VerifyOwner();
                Directory.Delete(root, recursive: true);
            }
            catch (Exception cleanupFailure) when (originalFailure is not null)
            {
                throw new AggregateException("Native record trait observation and private cleanup both failed.", originalFailure, cleanupFailure);
            }
        }
        ImmutableArray<SourceSnapshot> retained = snapshots.Select(snapshot => snapshot with
        {
            files = snapshot.files.Where(file => !Private(file.path)).ToImmutableArray(),
            searchDirectories = snapshot.searchDirectories.Where(directory => !Private(directory.path)).ToImmutableArray(),
            absentInputs = snapshot.absentInputs.Where(input => !Private(input.path)).ToImmutableArray(),
            excludedDirectories = snapshot.excludedDirectories.Where(directory => !PathEqual(directory, root)).ToImmutableArray()
        }).ToImmutableArray();
        if (SourceSnapshotBatch.GetMismatch(retained, token) is string mismatch)
            throw new IOException("Native record trait external inputs changed before return. " + mismatch);
        if (EnvironmentDigest() != environment) throw new IOException("The native trait execution environment changed during observation.");
        _ = Remaining();
        return observation! with { inputs = retained };

        bool Accessible(
            NativeDeclaration declaration,
            HashSet<StableId> pending
        ) {
            if (!pending.Add(declaration.id)) throw new InvalidOperationException("A cyclic declaration owner prevents native trait acquisition.");
            if (declaration.visibility is "Private" or "Protected") return false;
            if (declaration.kind == "Class" && (!declaration.templateParameters.IsEmpty
                || declaration.templateKind is not (null or "NormalClass" or "TemplateSpecializedClass"))) return false;
            return declaration.parent is not StableId parent || Accessible(declarations[parent], pending);
        }
        bool Named(NativeDeclaration declaration)
        {
            if (declaration.type is not StableId identity || module.GetType(identity).category != NativeTypeCategory.Record) return false;
            // An anonymous record can have a usable typedef spelling. Conversely,
            // a complete lexical child of an unbound template is not addressable.
            // Never manufacture a tag or use a parser-generated display name.
            return Regex.IsMatch(new NativeCallableAssertionEmitter(module).RenderStorageType(identity, string.Empty), C_TYPE_PATTERN,
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }
        bool Private(string path) => Within(path, root) || plan.scratchDirectory is string callerRoot && Within(path, callerRoot);
        SourceSnapshot Capture(ImmutableArray<string> files) => new ExternalInputSnapshotBuilder().Capture(files.Select(file => new DeclaredExternalInput(file, ExternalInputKind.File)).ToImmutableArray(), root, token);
        TimeSpan Remaining()
        {
            TimeSpan remaining = timeout - elapsed.Elapsed;
            if (remaining <= TimeSpan.Zero) throw new TimeoutException("The native record trait acquisition deadline elapsed.");
            return remaining;
        }
        void Verify()
        {
            VerifyOwner();
            token.ThrowIfCancellationRequested();
            if (SourceSnapshotBatch.GetMismatch(snapshots, token) is string mismatch) throw new IOException("Native record trait inputs changed during observation. " + mismatch);
            if (EnvironmentDigest() != environment) throw new IOException("The native trait execution environment changed during observation.");
        }
        void VerifyOwner()
        {
            if (!PathEqual(PhysicalPathResolver.Resolve(root), physical) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The native record trait scratch changed physical ownership.");
        }
        string Normalize(string value)
        {
            foreach ((string path, string logical) in (sourcePathMappings ?? ImmutableDictionary<string, string>.Empty).OrderByDescending(pair => pair.Key.Length))
                if (Path.EndsInDirectorySeparator(path))
                {
                    if (PathEqual(value, Path.TrimEndingDirectorySeparator(path))) return logical.TrimEnd('/', '\\');
                    if (value.StartsWith(path, PathComparison)) return logical + value[path.Length..];
                }
                else if (PathEqual(value, path)) return logical;
            value = OwnedRoot(value, root, "native-record-traits:/");
            return plan.scratchDirectory is string callerRoot ? OwnedRoot(value, callerRoot, "native-input:/") : value;
        }
        static string OwnedRoot(
            string value,
            string physical,
            string logical
        ) {
            foreach (string spelling in new[] { Path.TrimEndingDirectorySeparator(physical), Path.TrimEndingDirectorySeparator(physical).Replace('\\', '/') }.Distinct())
            {
                if (PathEqual(value, spelling)) return logical.TrimEnd('/');
                string prefix = spelling + (spelling.Contains('\\') ? "\\" : "/");
                if (value.StartsWith(prefix, PathComparison)) return logical + value[prefix.Length..];
            }
            return value;
        }
    }

    private static ImmutableArray<string> CompileArguments(
        ImmutableArray<string> original,
        NativePreprocessingOperation operation,
        string input,
        string output
    ) {
        ImmutableArray<string>.Builder arguments = ImmutableArray.CreateBuilder<string>();
        bool foundSource = false;
        foreach (string argument in original)
            if (argument.StartsWith("-flto", StringComparison.Ordinal) || argument.StartsWith("-fembed-bitcode", StringComparison.Ordinal))
                throw new NotSupportedException("Native record trait symbol sizes require a complete non-LTO target object.");
            else if (argument.StartsWith("-finput-charset=", StringComparison.Ordinal)
                && !argument["-finput-charset=".Length..].Equals("utf-8", StringComparison.OrdinalIgnoreCase)
                && !argument["-finput-charset=".Length..].Equals("utf8", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("Native record trait replay requires the completed compiler's UTF-8 token representation.");
        for (int index = 0; index < original.Length; index++)
        {
            string argument = original[index];
            if (PathEqual(argument, operation.source)) { foundSource = true; continue; }
            if (argument is "-o" or "-MF" or "-MT" or "-MQ" or "-x" or "-include" or "-imacros" or "-include-pch" or "-D" or "-U") { index++; continue; }
            if (argument is "-E" or "-P" or "-C" or "-CC" or "-dD" or "-dM" or "-M" or "-MM" or "-MD" or "-MMD" or "-MP" or "-c" or "-fsyntax-only"
                || argument.StartsWith("-D", StringComparison.Ordinal) || argument.StartsWith("-U", StringComparison.Ordinal)) continue;
            arguments.Add(argument);
        }
        if (!foundSource) throw new InvalidOperationException("The native trait query cannot identify the original preprocessing source argument.");
        return arguments.ToImmutable().AddRange(ImmutableArray.Create("-x", "c++-cpp-output", "-c", input, "-o", output));
    }

    private static Dictionary<string, bool> ReadSymbols(
        string text,
        HashSet<string> expected,
        bool trimUnderscore
    ) {
        Dictionary<string, bool> values = new(StringComparer.Ordinal);
        foreach (string line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            string symbol = parts[0];
            if (trimUnderscore && symbol.StartsWith('_')) symbol = symbol[1..];
            if (!symbol.StartsWith(C_SYMBOL_PREFIX, StringComparison.Ordinal)) continue;
            if (!expected.Contains(symbol) || parts.Length != 4 || parts[1] is not ("B" or "D" or "R")
                || !ulong.TryParse(parts[2], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _)
                || !ulong.TryParse(parts[3], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong size) || size is not (1 or 2)
                || !values.TryAdd(symbol, size == 2))
                throw new InvalidDataException("Native record trait inspection contains a malformed, duplicate or unsupported symbol size: " + symbol);
        }
        if (values.Count != expected.Count) throw new InvalidDataException("Native record trait inspection omitted a required symbol.");
        return values;
    }

    private static string EnvironmentDigest() => Digest(JsonSerializer.SerializeToUtf8Bytes(Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
        .Select(entry => new KeyValuePair<string, string>((string)entry.Key, (string)entry.Value!)).OrderBy(entry => entry.Key, StringComparer.Ordinal))).value;
    private static ContentDigest Digest(byte[] bytes) => new(Convert.ToHexStringLower(SHA256.HashData(bytes)));
    private static StringComparer Paths => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static bool PathEqual(
        string left,
        string right
    ) => string.Equals(left, right, PathComparison);
    private static bool Within(
        string path,
        string parent
    ) {
        string physical = PhysicalPathResolver.Resolve(Path.GetFullPath(path)), root = PhysicalPathResolver.Resolve(Path.GetFullPath(parent));
        return PathEqual(physical, root) || physical.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, PathComparison);
    }
}
