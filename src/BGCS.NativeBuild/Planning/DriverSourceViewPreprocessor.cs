using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Requests;
using BGCS.Infrastructure.Execution;
using BGCS.Infrastructure.IO;
using BGCS.NativeBuild.Execution;
using BGCS.Platforms.Targeting;

namespace BGCS.NativeBuild.Planning;

/// <summary>Obtains GNU-style driver tokens and final macros through independent dependency-checked operations.</summary>
internal static class DriverSourceViewPreprocessor
{
    internal static string? Family(FrontendRequest source)
    {
        if (source.language is not (SourceLanguage.C or SourceLanguage.Cpp) || source.target.toolchain.compilerPath is not string compiler) return null;
        string name = Path.GetFileNameWithoutExtension(compiler);
        if (name.Equals("clang-cl", StringComparison.OrdinalIgnoreCase)) return null;
        if (NativeCompilerTargeting.AcceptsClangTarget(compiler)) return "clang";
        return name.Contains("g++", StringComparison.Ordinal) || name.Contains("gcc", StringComparison.Ordinal)
            ? ToolchainDiscovery.GetCompilerDriverFamily(source.language, compiler)
                ?? throw new NotSupportedException("The selected gcc/g++ executable has no recognized native driver implementation identity.") : null;
    }

    internal static async ValueTask<NativeSourceViewResult> PrepareAsync(
        NativeSourceViewRequest request,
        CancellationToken cancellationToken
    ) {
        FrontendRequest source = request.source;
        string family = Family(source) ?? throw new NotSupportedException("The selected compiler has no GNU-style initial source-view capability.");
        string root = Path.GetFullPath(request.workingDirectory), compiler = Path.GetFullPath(source.target.toolchain.compilerPath!);
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()) throw new IOException("Native source view requires a new empty caller-owned scratch directory.");
        Directory.CreateDirectory(root);
        string input = Path.Combine(root, "source-view.input." + (source.language == SourceLanguage.C ? "c" : "cpp"));
        string marker = "BGCS_NATIVE_VIEW_CAPABILITY_" + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(input, new NativeFactInputEmitter().Emit(source.headers, source.preamble, source.postamble)
            + "\n#define " + marker + " 1\n", new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        bool windowsQuoting = source.arguments.Contains("--rsp-quoting=windows", StringComparer.Ordinal);
        NativeArgumentExpansionResult expandedArguments = new NativeArgumentInputSnapshotBuilder().Expand(source.arguments, source.pathBase,
            windowsQuoting, family == "clang", compiler, cancellationToken,
            source.snapshotExcludedDirectories.Add(root), source.includeDirectories.Concat(source.systemIncludeDirectories)
                .Concat(source.target.toolchain.systemIncludeFolders).Concat(source.target.toolchain.cxxSystemIncludeFolders), source.target.toolchain.sysRoot);
        ImmutableArray<string> options = NativeArguments(expandedArguments.arguments);
        NativeBuildRequest native = new(source.target, [new(input, source.language, source.languageStandard)], root, Path.Combine(root, "unused.o"))
        {
            argumentWorkingDirectory = source.pathBase,
            outputKind = NativeOutputKind.Object,
            snapshotExcludedDirectories = source.snapshotExcludedDirectories,
            includeDirectories = source.includeDirectories,
            systemIncludeDirectories = source.systemIncludeDirectories,
            defines = source.defines.AddRange(source.target.toolchain.defines.Where(define => !define.StartsWith("_MSC_VER=", StringComparison.Ordinal))),
            compilerArguments = options
        };
        NativeBuildPlan plan = new CompilerBuildProvider(family, compiler).PlanPreprocessing(native, cancellationToken);
        NativePreprocessingOperation original = plan.preprocessingOperations.Single();
        string macroOutput = Path.Combine(root, "final-macros.preprocessed." + (source.language == SourceLanguage.C ? "c" : "cpp"));
        string macroDependencies = Path.Combine(root, "final-macros.preprocessed.d");
        NativePreprocessingOperation macroPass = original with
        {
            outputFile = macroOutput,
            dependencyFile = original.dependencyFile! with { path = macroDependencies },
            command = original.command with
            {
                id = "preprocess-final-macros",
                arguments = original.command.arguments.Where(argument => argument != "-dD").Select(argument => argument == original.outputFile ? macroOutput
                    : argument == original.dependencyFile!.path ? macroDependencies : argument).ToImmutableArray().Add("-dM"),
                outputFiles = [macroOutput, macroDependencies]
            }
        };
        plan = plan with
        {
            snapshotExcludedDirectories = source.snapshotExcludedDirectories,
            dependencyScans = plan.dependencyScans.Select(scan => scan with
            { command = scan.command with { environmentOverrides = scan.command.environmentOverrides.SetItem("LC_ALL", "C") } }).ToImmutableArray(),
            includeSearchDirectories = plan.includeSearchDirectories.Concat(source.headerSearchPaths.Select(entry => entry.path))
                .Concat(source.headers.Select(header => Path.GetDirectoryName(Path.GetFullPath(header))!)).Distinct(Paths).ToImmutableArray(),
            preprocessingOperations = [original with { command = original.command with { arguments = original.command.arguments.Add("-CC"),
                environmentOverrides = original.command.environmentOverrides.SetItem("LC_ALL", "C") } },
                macroPass with { command = macroPass.command with { environmentOverrides = macroPass.command.environmentOverrides.SetItem("LC_ALL", "C") } }]
        };
        SourceSnapshot driverBefore = new ExternalInputSnapshotBuilder().Capture([new(compiler, ExternalInputKind.File)], source.pathBase, cancellationToken);
        ProcessExecutionResult version = await Query(["--version"]).ConfigureAwait(false);
        string reportedVersion = version.standardOutput + version.standardError;
        if (family == "clang" ? !reportedVersion.Contains("clang version", StringComparison.OrdinalIgnoreCase)
            : reportedVersion.Contains("clang version", StringComparison.OrdinalIgnoreCase) || !reportedVersion.Contains("Free Software Foundation", StringComparison.Ordinal))
            throw new IOException("The selected native compiler's reported implementation differs from its source-view family.");
        string reportedProgram = family == "gnu" ? source.language == SourceLanguage.C ? "cc1" : "cc1plus" : "clang";
        ProcessExecutionResult frontend = await Query(options.Add("-print-prog-name=" + reportedProgram)).ConfigureAwait(false);
        string reported = frontend.standardOutput.Trim();
        if (reported.Any(character => character is '\r' or '\n' or '\0')) throw new IOException("The selected compiler reported an ambiguous frontend program identity.");
        string reportedPath = Path.IsPathFullyQualified(reported) ? reported : Path.Combine(Path.GetDirectoryName(compiler)!, reported);
        if (OperatingSystem.IsWindows() && !File.Exists(reportedPath) && File.Exists(reportedPath + ".exe")) reportedPath += ".exe";
        string? frontendPath = NativeBuildToolDiscovery.FindExecutable(reportedPath);
        if (frontendPath is null) throw new NotSupportedException("The selected native compiler did not identify its existing language frontend: " + reportedProgram);
        ImmutableArray<DeclaredExternalInput>.Builder componentInputs = ImmutableArray.CreateBuilder<DeclaredExternalInput>();
        componentInputs.Add(new(compiler, ExternalInputKind.File));
        // Clang commonly reports this exact driver path as its frontend. Keep
        // distinct logical spellings and aliases as independent dependencies.
        if (!StringComparer.Ordinal.Equals(compiler, frontendPath)) componentInputs.Add(new(frontendPath, ExternalInputKind.File));
        string[] componentDirectories = new[] { compiler, frontendPath }.Select(path => Path.GetDirectoryName(PhysicalPathResolver.Resolve(path))!).Distinct(Paths).ToArray();
        if (OperatingSystem.IsWindows())
            foreach (string directory in componentDirectories)
                componentInputs.AddRange(Directory.EnumerateFiles(directory, "*.dll").Select(path => new DeclaredExternalInput(path, ExternalInputKind.File)));
        SourceSnapshot components = new ExternalInputSnapshotBuilder().Capture(componentInputs.ToImmutable(), source.pathBase, cancellationToken);
        components = components with { searchDirectories = componentDirectories.Select(directory => DirectoryMembershipDigest.Capture(directory, cancellationToken: cancellationToken)).ToImmutableArray() };
        if (!SourceSnapshotVerifier.Matches(driverBefore, cancellationToken)) throw new IOException("The selected native driver changed during component discovery.");
        NativeBuildExecutionResult executed = await new PlannedNativeBuildExecutor().PreprocessWithDependenciesAsync(plan,
            TimeSpan.FromSeconds(request.timeoutSeconds), cancellationToken).ConfigureAwait(false);
        byte[] preprocessedBytes = await File.ReadAllBytesAsync(original.outputFile, cancellationToken).ConfigureAwait(false);
        ContentDigest preprocessedDigest = new(Convert.ToHexStringLower(SHA256.HashData(preprocessedBytes)));
        string tokens;
        using (StreamReader reader = new(new MemoryStream(preprocessedBytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            tokens = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        string dump = await File.ReadAllTextAsync(macroOutput, cancellationToken).ConfigureAwait(false);
        ImmutableArray<NativeMacroDirectiveReader.Definition> definitions = NativeMacroDirectiveReader.Read(dump, rawStrings: source.language == SourceLanguage.Cpp);
        if (!definitions.Any(definition => definition.name == marker && !definition.functionLike && definition.replacement == "1"))
            throw new NotSupportedException("The selected native compiler did not produce the required final -dM macro state.");
        var macroFacts = await Macros(source, native, plan, root, definitions.Where(definition => definition.name != marker).ToImmutableArray(), cancellationToken).ConfigureAwait(false);
        ImmutableArray<BindingDiagnostic> diagnostics = NativeSourceViewDiagnostics.Read(executed.executions, source).AddRange(macroFacts.diagnostics);
        if (SourceSnapshotBatch.GetMismatch([executed.inputSnapshot, components, expandedArguments.snapshot], cancellationToken) is not null)
            throw new IOException("Native driver components or source inputs changed during source-view extraction.");
        SourceSnapshot retained = new(executed.inputSnapshot.files.Concat(executed.argumentInputs.files).Concat(components.files).Concat(expandedArguments.snapshot.files)
                .Where(dependency => !Within(dependency.path, root)).DistinctBy(dependency => dependency.path, Paths).ToImmutableArray(),
            executed.inputSnapshot.searchDirectories.Concat(executed.argumentInputs.searchDirectories).Concat(components.searchDirectories).Concat(expandedArguments.snapshot.searchDirectories)
                .Where(dependency => !Within(dependency.path, root)).DistinctBy(dependency => dependency.path, Paths).ToImmutableArray())
        {
            absentInputs = executed.inputSnapshot.absentInputs.Concat(executed.argumentInputs.absentInputs).Concat(expandedArguments.snapshot.absentInputs).Where(dependency => !Within(dependency.path, root))
                .DistinctBy(dependency => dependency.path, Paths).ToImmutableArray(),
            excludedDirectories = source.snapshotExcludedDirectories.Add(root)
        };
        NativeSourceViewIdentity sourceIdentity = new(input, source);
        string rawTrace = tokens;
        foreach (var capability in NativeMacroDirectiveReader.Read(rawTrace).Where(definition => definition.name == marker).OrderByDescending(definition => definition.start))
            rawTrace = rawTrace.Remove(capability.start, capability.length);
        NativeDeclarationPrelude? declarationPrelude = null;
        if (family == "gnu" && source.language == SourceLanguage.Cpp)
        {
            ImmutableArray<string> authenticated = ToolchainDiscovery.DiscoverHeaderSearchPaths(source.language, compiler, source.target.toolchain.sysRoot)
                .Select(entry => entry.path).ToImmutableArray();
            (tokens, declarationPrelude) = NativeGnuDeclarationPrelude.Extract(rawTrace, authenticated,
                executed.inputSnapshot.files.Select(file => file.path).ToImmutableArray(), cancellationToken);
        }
        string normalized = sourceIdentity.Normalize(tokens);
        string identity = version.standardOutput + version.standardError;
        byte[] observation = JsonSerializer.SerializeToUtf8Bytes(new
        {
            revision = 5,
            diagnosticLocale = new { LC_ALL = "C" },
            family,
            identity,
            compilerInputs = components.files.OrderBy(component => component.path, StringComparer.Ordinal).ToImmutableArray(),
            source = normalized,
            nativeTrace = sourceIdentity.Normalize(rawTrace),
            declarationPrelude,
            commandLine = macroFacts.commandLineFingerprint,
            macros = macroFacts.macros.Select(macro => macro with { location = sourceIdentity.Map(macro.location) }).ToImmutableArray(),
            diagnostics = diagnostics.Select(diagnostic => diagnostic with { location = sourceIdentity.Map(diagnostic.location) }).ToImmutableArray()
        });
        return new(tokens, macroFacts.macros, retained)
        {
            compilerFamily = family,
            compilerIdentity = identity,
            declarationPrelude = declarationPrelude,
            compilation = declarationPrelude is null ? null : new(plan, plan.preprocessingOperations[0], preprocessedDigest),
            diagnostics = diagnostics,
            observationFingerprint = new(Convert.ToHexStringLower(SHA256.HashData(observation)))
        };

        async ValueTask<ProcessExecutionResult> Query(ImmutableArray<string> arguments)
        {
            if (family == "clang") arguments = arguments.Insert(0, "--no-default-config");
            ProcessExecutionResult result = await ProcessExecutor.ExecuteAsync(compiler, arguments, source.pathBase,
                TimeSpan.FromSeconds(request.timeoutSeconds), cancellationToken, environmentOverrides: NativeBuildEnvironment.cleanCompiler.SetItem("LC_ALL", "C")).ConfigureAwait(false);
            if (result.timedOut || result.exitCode != 0) throw new NotSupportedException("The selected compiler could not report its native preprocessing identity: " + result.standardError);
            return result;
        }
    }

    private static async ValueTask<(ImmutableArray<NativeSourceMacro> macros, ImmutableArray<BindingDiagnostic> diagnostics, string commandLineFingerprint)> Macros(
        FrontendRequest source,
        NativeBuildRequest native,
        NativeBuildPlan plan,
        string root,
        ImmutableArray<NativeMacroDirectiveReader.Definition> definitions,
        CancellationToken cancellationToken
    ) {
        Dictionary<string, NativeMacroDirectiveReader.Definition> final = definitions.ToDictionary(definition => definition.name, StringComparer.Ordinal);
        Dictionary<string, List<(NativeMacroDirectiveReader.Definition definition, string file)>> origins = new(StringComparer.Ordinal);
        bool rawStrings = source.language == SourceLanguage.Cpp;
        NativeCommandLineMacroOrigins commandLine = new(plan.preprocessingOperations.Single(operation => operation.command.id == "preprocess-final-macros").command.arguments,
            msvc: false, rawStrings: rawStrings);
        HashSet<string> sourceChanges = NativeMacroDirectiveReader.ChangedNames(source.preamble, rawStrings)
            .Union(NativeMacroDirectiveReader.ChangedNames(source.postamble, rawStrings)).ToHashSet(StringComparer.Ordinal);
        HashSet<string> userNames = commandLine.names.Concat(NativeMacroDirectiveReader.Read(source.preamble, rawStrings: source.language == SourceLanguage.Cpp)
            .Concat(NativeMacroDirectiveReader.Read(source.postamble, rawStrings: source.language == SourceLanguage.Cpp)).Select(definition => definition.name)).ToHashSet(StringComparer.Ordinal);
        List<string> systems = source.systemIncludeDirectories.Concat(source.target.toolchain.systemIncludeFolders)
            .Concat(source.target.toolchain.cxxSystemIncludeFolders).Concat(source.headerSearchPaths.Select(entry => entry.path)).ToList();
        for (int index = 0; index < native.compilerArguments.Length; index++)
        {
            string option = native.compilerArguments[index];
            if (option is "-isystem" or "-idirafter" or "-iframework")
            { if (++index >= native.compilerArguments.Length) throw new ArgumentException("A native system include option omitted its value."); systems.Add(native.compilerArguments[index]); }
        }
        string[] systemRoots = systems.Select(path => Path.GetFullPath(path, source.pathBase).TrimEnd('\\', '/') + Path.DirectorySeparatorChar).Distinct(Paths).ToArray();
        foreach (string dependency in BuildDependencyCollector.Read(plan.dependencyScans.Select(scan => scan.dependencyFile), root, cancellationToken).Where(path => !Within(path, root)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string contents = await File.ReadAllTextAsync(dependency, cancellationToken).ConfigureAwait(false);
            sourceChanges.UnionWith(NativeMacroDirectiveReader.ChangedNames(contents, rawStrings));
            foreach (var candidate in NativeMacroDirectiveReader.Read(contents, rawStrings: source.language == SourceLanguage.Cpp))
            {
                if (!System(dependency)) userNames.Add(candidate.name);
                if (!final.TryGetValue(candidate.name, out var actual) || candidate.functionLike != actual.functionLike
                    || !candidate.parameters.SequenceEqual(actual.parameters, StringComparer.Ordinal) || candidate.tokens != actual.tokens) continue;
                if (!origins.TryGetValue(candidate.name, out var locations)) origins.Add(candidate.name, locations = []);
                locations.Add((candidate, dependency));
            }
        }
        var diagnostics = ImmutableArray.CreateBuilder<BindingDiagnostic>();
        var macros = ImmutableArray.CreateBuilder<NativeSourceMacro>();
        foreach (var definition in final.Values.OrderBy(definition => definition.name, StringComparer.Ordinal))
        {
            origins.TryGetValue(definition.name, out var candidates);
            SourceLocation? location = candidates?.Count == 1 ? new(candidates[0].file, candidates[0].definition.line, 1) : null;
            bool commandLineChanged = commandLine.Declares(definition.name) && sourceChanges.Contains(definition.name);
            if (commandLineChanged) location = null;
            bool system = !userNames.Contains(definition.name) && (candidates is null || candidates.All(candidate => System(candidate.file)));
            if (location is null && !system)
            {
                bool declared = !commandLineChanged && commandLine.Matches(definition);
                diagnostics.Add(new(declared ? BindingDiagnosticSeverity.Information : BindingDiagnosticSeverity.Warning,
                    declared ? "Native macro '" + definition.name + "' exactly matches its declared command-line definition (provenance sha256:" + commandLine.fingerprint + "); it has no header source location."
                        : "Native macro '" + definition.name + "' has no unique established original definition; its final value is preserved with unknown source origin.",
                    "BGCS-NATIVE-MACRO-ORIGIN"));
            }
            macros.Add(new(definition.name, definition.replacement, definition.functionLike, definition.parameters, location, system));
        }
        return (macros.ToImmutable(), diagnostics.ToImmutable(), commandLine.fingerprint);
        bool System(string path) => systemRoots.Any(directory => Path.GetFullPath(path).StartsWith(directory, Comparison));
    }

    private static ImmutableArray<string> NativeArguments(ImmutableArray<string> arguments)
    {
        var result = ImmutableArray.CreateBuilder<string>();
        foreach (string option in arguments)
        {
            if (option is "-fparse-all-comments") continue;
            if (option is "-E" or "-P" or "-dM" or "-dD" or "-dN" or "-o" or "-M" or "-MM" or "-MD" or "-MMD" or "-MF" or "-MT" or "-MQ" or "-MG"
                || option.StartsWith("-Wp,", StringComparison.Ordinal) || option.StartsWith("-Xpreprocessor", StringComparison.Ordinal))
                throw new NotSupportedException("An initial native source view cannot replace its controlled preprocessing, dependency or output operations: " + option);
            result.Add(option);
        }
        return result.ToImmutable();
    }

    private static StringComparer Paths => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static bool Within(
        string path,
        string root
    ) => Path.GetFullPath(path).StartsWith(root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, Comparison)
        || Path.GetFullPath(path).Equals(root, Comparison);
}
