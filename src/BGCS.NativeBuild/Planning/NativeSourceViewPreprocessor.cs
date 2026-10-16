using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
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
using BGCS.Infrastructure.IO;
using BGCS.NativeBuild.Execution;
using BGCS.NativeBuild.Validation;
using BGCS.Platforms.Toolchains;

namespace BGCS.NativeBuild.Planning;

/// <summary>Extracts the selected native compiler's initial source view without compiling or linking a product.</summary>
public sealed class NativeSourceViewPreprocessor
{
    /// <summary>Reports the currently implemented native compiler source-view capability.</summary>
    /// <param name="source">The resolved original frontend request.</param>
    /// <returns>Whether this implementation can obtain the exact selected compiler's preprocessing view.</returns>
    public static bool IsSupported(FrontendRequest source) => source.language is SourceLanguage.C or SourceLanguage.Cpp
        && (source.target.toolchain.compilerPath is string compiler && Path.GetFileName(compiler).Equals("cl.exe", StringComparison.OrdinalIgnoreCase)
            || DriverSourceViewPreprocessor.Family(source) is not null);

    /// <summary>Runs the native preprocessor with source options while preserving independent native-build flags for later verification.</summary>
    /// <param name="request">The original source configuration and caller-owned private outputs.</param>
    /// <param name="cancellationToken">Cancels preprocessing, dependency scans and macro-origin reads.</param>
    /// <returns>Native-expanded tokens, active macro definitions and verified original inputs.</returns>
    public async ValueTask<NativeSourceViewResult> PrepareAsync(
        NativeSourceViewRequest request,
        CancellationToken cancellationToken = default
    ) {
        ArgumentNullException.ThrowIfNull(request);
        if (request.timeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(request));
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(request.timeoutSeconds));
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try { return await PrepareCoreAsync(request, linked.Token).ConfigureAwait(false); }
        catch (OperationCanceledException failure)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (deadline.IsCancellationRequested) throw new TimeoutException("The native source-view extraction deadline elapsed.", failure);
            throw;
        }
    }

    private static async ValueTask<NativeSourceViewResult> PrepareCoreAsync(
        NativeSourceViewRequest request,
        CancellationToken cancellationToken
    ) {
        FrontendRequest source = request.source;
        if (!IsSupported(source)) throw new NotSupportedException("The selected native compiler has no implemented initial source-view provider.");
        if (DriverSourceViewPreprocessor.Family(source) is not null)
            return await DriverSourceViewPreprocessor.PrepareAsync(request, cancellationToken).ConfigureAwait(false);
        if (source.language is not (SourceLanguage.C or SourceLanguage.Cpp)) throw new NotSupportedException("Native source views currently require C or C++.");
        string root = Path.GetFullPath(request.workingDirectory);
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
            throw new IOException("Native source view requires a new empty caller-owned scratch directory.");
        Directory.CreateDirectory(root);
        string marker = "BGCS_NATIVE_VIEW_CAPABILITY_" + Guid.NewGuid().ToString("N");
        string input = Path.Combine(root, "source-view.input." + (source.language == SourceLanguage.C ? "c" : "cpp"));
        string original = new NativeFactInputEmitter().Emit(source.headers, source.preamble, source.postamble)
            + "\n#define " + marker + " 1\n";
        await File.WriteAllTextAsync(input, original, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        string compiler = source.target.toolchain.compilerPath!;
        MsvcToolchainInfo sdk = MsvcToolchainDiscovery.Resolve(compiler, source.target.architectureId,
            source.target.toolchain.sysRoot, source.target.toolchain.sdkVersion);
        if (source.target.toolchain.compilerVersion is string expectedCompiler && expectedCompiler != sdk.compilerVersion.ToString())
            throw new IOException("The native source-view compiler version changed after target resolution.");
        // cl.exe loads the language frontend and support DLLs from its tool directory.
        // Freeze all directory bytes, links and membership before its first operation,
        // rather than treating the driver executable alone as the compiler implementation.
        SourceSnapshot components = new ExternalInputSnapshotBuilder().Capture(
            [new(Path.GetDirectoryName(compiler)!, ExternalInputKind.Directory)], source.pathBase, cancellationToken);
        NativeBuildRequest native = new(source.target, [new(input, source.language, source.languageStandard)], root, Path.Combine(root, "unused.obj"))
        {
            argumentWorkingDirectory = source.pathBase,
            outputKind = NativeOutputKind.Object,
            snapshotExcludedDirectories = source.snapshotExcludedDirectories,
            includeDirectories = source.includeDirectories,
            systemIncludeDirectories = source.systemIncludeDirectories.Concat(source.target.toolchain.systemIncludeFolders)
                .Concat(source.target.toolchain.cxxSystemIncludeFolders).Distinct(StringComparer.OrdinalIgnoreCase).ToImmutableArray(),
            defines = source.defines.AddRange(source.target.toolchain.defines.Where(define => !define.StartsWith("_MSC_VER=", StringComparison.Ordinal))),
            compilerArguments = NativeArguments(source, sdk.compilerVersion)
        };
        NativeBuildPlan plan = new MsvcBuildProvider(sdk).Plan(native, cancellationToken);
        plan = plan with
        {
            snapshotExcludedDirectories = source.snapshotExcludedDirectories,
            dependencyScans = plan.dependencyScans.Select(scan => scan with
            { command = scan.command with { environmentOverrides = scan.command.environmentOverrides.SetItem("VSLANG", "1033") } }).ToImmutableArray(),
            includeSearchDirectories = plan.includeSearchDirectories.Concat(source.headers.Select(header => Path.GetDirectoryName(Path.GetFullPath(header))!))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToImmutableArray(),
            preprocessingOperations = plan.preprocessingOperations.Select(operation => operation with
            {
                command = operation.command with
                {
                    arguments = operation.command.arguments.Add("/C").Add("/PD"),
                    environmentOverrides = operation.command.environmentOverrides.SetItem("VSLANG", "1033")
                }
            }).ToImmutableArray()
        };
        NativeBuildExecutionResult executed = await new PlannedNativeBuildExecutor().PreprocessWithDependenciesAsync(plan,
            TimeSpan.FromSeconds(request.timeoutSeconds), cancellationToken).ConfigureAwait(false);
        if (!SourceSnapshotVerifier.Matches(components, cancellationToken)) throw new IOException("Native compiler components changed during source preprocessing.");
        NativePreprocessingOperation preprocessing = plan.preprocessingOperations.Single();
        string expanded = await File.ReadAllTextAsync(preprocessing.outputFile, cancellationToken).ConfigureAwait(false);
        bool rawStrings = source.language == SourceLanguage.Cpp;
        ImmutableArray<NativeMacroDirectiveReader.Definition> definitions = NativeMacroDirectiveReader.Read(expanded, msvcMacroDump: true, rawStrings: rawStrings);
        if (!definitions.Any(definition => definition.name == marker && !definition.functionLike && definition.replacement == "1"))
            throw new NotSupportedException("The selected MSVC compiler did not provide the required conformant /PD macro dump; no faithful initial source view is available.");
        ImmutableArray<string> systemDirectories = native.systemIncludeDirectories.Concat(native.compilerArguments
                .Where(argument => argument.StartsWith("/external:I", StringComparison.Ordinal)).Select(argument => Path.GetFullPath(argument["/external:I".Length..], source.pathBase)))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToImmutableArray();
        string[] systemRoots = systemDirectories.Select(path => Path.GetFullPath(path).TrimEnd('\\', '/') + Path.DirectorySeparatorChar).ToArray();
        Dictionary<string, NativeMacroDirectiveReader.Definition> final = definitions.Where(definition => definition.name != marker)
            .ToDictionary(definition => definition.name, StringComparer.Ordinal);
        Dictionary<string, List<(NativeMacroDirectiveReader.Definition definition, string file)>> origins = new(StringComparer.Ordinal);
        NativeCommandLineMacroOrigins commandLine = new(preprocessing.command.arguments, msvc: true, rawStrings: rawStrings);
        HashSet<string> sourceChanges = NativeMacroDirectiveReader.ChangedNames(source.preamble, rawStrings)
            .Union(NativeMacroDirectiveReader.ChangedNames(source.postamble, rawStrings)).ToHashSet(StringComparer.Ordinal);
        HashSet<string> userNames = commandLine.names
            .Concat(NativeMacroDirectiveReader.Read(source.preamble, rawStrings: rawStrings).Concat(NativeMacroDirectiveReader.Read(source.postamble, rawStrings: rawStrings)).Select(definition => definition.name))
            .ToHashSet(StringComparer.Ordinal);
        ImmutableArray<string> headerInputs = BuildDependencyCollector.Read(plan.dependencyScans.Select(scan => scan.dependencyFile), root, cancellationToken);
        foreach (string dependency in headerInputs.Where(path => !Within(path, root)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string contents = await File.ReadAllTextAsync(dependency, cancellationToken).ConfigureAwait(false);
            sourceChanges.UnionWith(NativeMacroDirectiveReader.ChangedNames(contents, rawStrings));
            foreach (NativeMacroDirectiveReader.Definition candidate in NativeMacroDirectiveReader.Read(contents, rawStrings: rawStrings))
            {
                if (!System(dependency)) userNames.Add(candidate.name);
                if (!final.TryGetValue(candidate.name, out var actual) || candidate.functionLike != actual.functionLike
                    || !candidate.parameters.SequenceEqual(actual.parameters, StringComparer.Ordinal) || candidate.tokens != actual.tokens) continue;
                if (!origins.TryGetValue(candidate.name, out var candidates)) origins.Add(candidate.name, candidates = []);
                candidates.Add((candidate, dependency));
            }
        }
        ImmutableArray<BindingDiagnostic>.Builder diagnostics = ImmutableArray.CreateBuilder<BindingDiagnostic>();
        diagnostics.AddRange(NativeSourceViewDiagnostics.Read(executed.executions, source));
        ImmutableArray<NativeSourceMacro>.Builder macros = ImmutableArray.CreateBuilder<NativeSourceMacro>();
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
        StringBuilder stripped = new();
        int copied = 0;
        foreach (var definition in definitions)
        {
            stripped.Append(expanded, copied, definition.start - copied);
            // /PD orders its dump by compiler hash buckets. A per-request capability
            // macro can change that order. Remove only the discarded directive bytes,
            // retaining all line breaks, so dump whitespace cannot change source identity.
            for (int index = definition.start; index < definition.start + definition.length; index++)
                if (expanded[index] is '\r' or '\n') stripped.Append(expanded[index]);
            copied = definition.start + definition.length;
        }
        stripped.Append(expanded, copied, expanded.Length - copied);
        string tokens = NativeEffectiveSourceAdapter.PreserveSystemHeaderMarkers(stripped.ToString(), systemDirectories);
        if (!SourceSnapshotVerifier.Matches(executed.inputSnapshot, cancellationToken) || !SourceSnapshotVerifier.Matches(components, cancellationToken))
            throw new IOException("Native preprocessing inputs or compiler components changed during source-view extraction.");
        SourceSnapshot retained = executed.inputSnapshot with
        {
            files = executed.inputSnapshot.files.Concat(executed.argumentInputs.files).Concat(components.files).Where(dependency => !Within(dependency.path, root))
                .DistinctBy(dependency => dependency.path, StringComparer.OrdinalIgnoreCase).ToImmutableArray(),
            searchDirectories = executed.inputSnapshot.searchDirectories.Concat(executed.argumentInputs.searchDirectories).Concat(components.searchDirectories).Where(dependency => !Within(dependency.path, root))
                .DistinctBy(dependency => dependency.path, StringComparer.OrdinalIgnoreCase).ToImmutableArray(),
            absentInputs = executed.inputSnapshot.absentInputs.Concat(executed.argumentInputs.absentInputs).Where(dependency => !Within(dependency.path, root))
                .DistinctBy(dependency => dependency.path, StringComparer.OrdinalIgnoreCase).ToImmutableArray(),
            excludedDirectories = source.snapshotExcludedDirectories.Add(root)
        };
        ImmutableArray<NativeSourceMacro> frozenMacros = macros.ToImmutable();
        NativeSourceViewIdentity identity = new(input, source);
        string normalized = identity.Normalize(tokens);
        byte[] observation = JsonSerializer.SerializeToUtf8Bytes(new
        {
            revision = 4,
            diagnosticLocale = new { VSLANG = "1033" },
            compilerIdentity = sdk.compilerVersion.ToString(),
            compilerInputs = components.files.OrderBy(component => component.path, StringComparer.Ordinal).ToImmutableArray(),
            source = normalized,
            commandLine = commandLine.fingerprint,
            macros = frozenMacros.Select(macro => macro with { location = identity.Map(macro.location) }).ToImmutableArray(),
            diagnostics = diagnostics.Select(diagnostic => diagnostic with { location = identity.Map(diagnostic.location) }).ToImmutableArray()
        });
        return new(tokens, frozenMacros, retained)
        {
            compilerIdentity = sdk.compilerVersion.ToString(),
            diagnostics = diagnostics.ToImmutable(),
            observationFingerprint = new(Convert.ToHexStringLower(SHA256.HashData(observation)))
        };

        bool System(string file) => systemRoots.Any(directory => Path.GetFullPath(file).StartsWith(directory, StringComparison.OrdinalIgnoreCase));
    }

    private static bool Within(
        string path,
        string root
    ) {
        string absolute = Path.GetFullPath(path), parent = Path.GetFullPath(root).TrimEnd('\\', '/');
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return absolute.Equals(parent, comparison) || absolute.StartsWith(parent + Path.DirectorySeparatorChar, comparison);
    }

    private static ImmutableArray<string> NativeArguments(
        FrontendRequest source,
        Version compiler
    ) {
        ImmutableArray<string>.Builder result = ImmutableArray.CreateBuilder<string>();
        string compatibility = "-fms-compatibility-version=" + compiler.Major.ToString(CultureInfo.InvariantCulture) + "." + compiler.Minor.ToString(CultureInfo.InvariantCulture);
        for (int index = 0; index < source.arguments.Length; index++)
        {
            string argument = source.arguments[index];
            if (argument.StartsWith("-Wp,", StringComparison.Ordinal) || argument.StartsWith("-Wl,", StringComparison.Ordinal) || argument.StartsWith("-Wa,", StringComparison.Ordinal))
                throw new NotSupportedException("The selected MSVC initial source view has no faithful mapping for forwarded driver options: " + argument);
            if (argument == compatibility || argument is "-fms-compatibility" or "-fms-extensions" or "-fparse-all-comments" or "-w" || argument.StartsWith("-W", StringComparison.Ordinal)) continue;
            if (argument is "-D" or "-U" or "-I" or "-include" or "-isystem")
            {
                if (++index >= source.arguments.Length) throw new ArgumentException("An initial native source option omitted its value: " + argument);
                result.Add((argument == "-include" ? "/FI" : argument == "-isystem" ? "/external:I" : "/" + argument[1..]) + source.arguments[index]);
            }
            else if (argument.StartsWith("-D", StringComparison.Ordinal) || argument.StartsWith("-U", StringComparison.Ordinal) || argument.StartsWith("-I", StringComparison.Ordinal)) result.Add("/" + argument[1..]);
            else if (argument == "-funsigned-char") result.Add("/J");
            else if (argument.StartsWith("-fpack-struct=", StringComparison.Ordinal)) result.Add("/Zp" + argument["-fpack-struct=".Length..]);
            else throw new NotSupportedException("The selected MSVC initial source view has no faithful mapping for parser option: " + argument);
        }
        return result.ToImmutable();
    }
}
