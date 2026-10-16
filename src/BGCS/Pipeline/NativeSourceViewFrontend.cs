using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.Analysis.Interop;
using BGCS.Composition;
using BGCS.Infrastructure.IO;
using BGCS.NativeBuild.Planning;
using BGCS.NativeBuild.Validation;

namespace BGCS;

internal static class NativeSourceViewFrontend
{
    internal static async Task<FrontendResult> ParseAsync(
        IFrontend frontend,
        FrontendRequest source,
        TimeSpan timeout,
        GenerationInputTracker inputs,
        CancellationToken cancellationToken,
        FrontendGenerationCache? frontendCache = null
    ) {
        string temporaryParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string root = Path.GetFullPath(Path.Combine(temporaryParent, "bgcs-native-source-view-" + Guid.NewGuid().ToString("N")));
        if (!root.StartsWith(temporaryParent + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new IOException("Native source-view scratch escaped its intended temporary directory.");
        Directory.CreateDirectory(root);
        using CancellationTokenSource deadline = new(timeout);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            string nativeRoot = Path.Combine(root, "native");
            NativeSourceViewResult view = await new NativeSourceViewPreprocessor().PrepareAsync(new(source, nativeRoot)
            { timeoutSeconds = checked((int)Math.Ceiling(timeout.TotalSeconds)) }, linked.Token).ConfigureAwait(false);
            inputs.CaptureNativeSourceObservation(source.target.targetId.value, view.observationFingerprint, view.inputs, nativeRoot, source.sourcePathMappings);
            inputs.CaptureObservation(Replay);
            FrontendGenerationCache.Attempt? cached = frontendCache?.Prepare(source, frontend, linked.Token, view);
            if (cached?.result is FrontendResult restored) return restored;
            string parserRoot = Path.Combine(root, "parser");
            Directory.CreateDirectory(parserRoot);
            string input = Path.Combine(parserRoot, "primary.preprocessed." + (source.language == SourceLanguage.C ? "h" : "hpp"));
            StringBuilder content = new(view.expandedSource);
            foreach (NativeSourceMacro macro in view.macros)
            {
                string file = macro.location?.file ?? Path.Combine(source.pathBase, "native-macros", source.target.targetId.value, macro.name + ".h");
                if (file.Any(character => character is '\r' or '\n' or '\0')) throw new IOException("A native macro origin cannot become controlled line metadata.");
                content.Append("\n# ").Append(macro.location?.line ?? 1).Append(" \"")
                    .Append(file.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal))
                    .Append('"').Append(macro.isSystem ? " 3" : string.Empty).AppendLine();
                content.Append("#define ").Append(macro.name);
                if (macro.isFunctionLike) content.Append('(').Append(string.Join(",", macro.parameters)).Append(')');
                content.Append(' ').AppendLine(macro.replacement);
            }
            await File.WriteAllTextAsync(input, content.ToString(), new UTF8Encoding(false), linked.Token).ConfigureAwait(false);
            string logical = "native-source-view/" + source.target.targetId.value + "/primary." + (source.language == SourceLanguage.C ? "h" : "hpp");
            var originalToolchain = source.target.toolchain;
            NativeToolchainDescriptor toolchain = new(originalToolchain.compilerPath, originalToolchain.sysRoot,
                originalToolchain.systemIncludeFolders, [], [], originalToolchain.cxxSystemIncludeFolders,
                originalToolchain.sdkVersion, originalToolchain.compilerVersion, originalToolchain.linkerPath, originalToolchain.libraryDirectories);
            NativeTargetDescriptor target = new(source.target.targetId, source.target.platformId, source.target.architectureId,
                source.target.abiId, source.target.triple, toolchain, source.target.capabilities, source.target.byteOrder);
            FrontendRequest parsedSource = source with
            {
                target = target,
                arguments = ParserArguments(source.arguments),
                headers = [input],
                defines = [],
                preamble = NativeEffectiveSourceAdapter.ParserPreamble(view.compilerFamily, source.language),
                postamble = string.Empty,
                sourcePathMappings = source.sourcePathMappings.SetItem(input, logical),
                usePresumedSourceLocations = true,
                snapshotExcludedDirectories = source.snapshotExcludedDirectories.Add(root)
            };
            // Primary capture already belongs to this session's cache inputs.
            // Revalidate the effective parse without adding a second registration
            // only on cold parses, which would make warm emission keys differ.
            parsedSource = await WorkerPluginPreparation.PrepareActivationsAsync(parsedSource, frontend, null, linked.Token).ConfigureAwait(false);
            if (source.workerPluginCapture is { } expectedWorkerCapture
                && !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(expectedWorkerCapture), JsonSerializer.SerializeToElement(parsedSource.workerPluginCapture)))
                throw new IOException("Worker extension state changed while preparing the effective native source view.");
            FrontendResult parsed = await frontend.ParseAsync(parsedSource, linked.Token).ConfigureAwait(false);
            if (parsed.snapshot.excludedDirectories.Any(directory => !parsedSource.snapshotExcludedDirectories.Contains(directory,
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)))
                throw new IOException("The native source-view frontend excluded an undeclared search directory.");
            if (!SourceSnapshotVerifier.Matches(parsed.snapshot, linked.Token)) throw new IOException("Native source-view parser inputs changed during extraction.");
            if (parsed.executionInputs is { } execution && !SourceSnapshotVerifier.Matches(execution, linked.Token))
                throw new IOException("Native source-view frontend execution resources changed during extraction.");
            var unknown = view.macros.Where(macro => macro.location is null).Select(macro => macro.name).ToHashSet(StringComparer.Ordinal);
            var declarations = parsed.module.declarations.Select(declaration => declaration.kind == "Macro" && unknown.Contains(declaration.name)
                ? declaration with { source = new SourceLocation("native-macros/" + source.target.targetId.value + "/" + declaration.name, 0, 0) }
                : declaration).ToImmutableArray();
            SourceSnapshot snapshot = new(view.inputs.files.Concat(parsed.snapshot.files.Where(dependency => !Within(dependency.path)))
                    .DistinctBy(dependency => dependency.path, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToImmutableArray(),
                view.inputs.searchDirectories.Concat(parsed.snapshot.searchDirectories.Where(dependency => !Within(dependency.path)))
                    .DistinctBy(dependency => dependency.path, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToImmutableArray())
            {
                absentInputs = view.inputs.absentInputs.Concat(parsed.snapshot.absentInputs.Where(dependency => !Within(dependency.path)))
                    .DistinctBy(dependency => dependency.path, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToImmutableArray(),
                excludedDirectories = source.snapshotExcludedDirectories
            };
            NativeModule module = new(parsed.module.targetId, parsed.module.triple, parsed.module.pointerSize, parsed.module.types,
                declarations, parsed.module.diagnostics.AddRange(view.diagnostics))
            { language = parsed.module.language, byteOrder = parsed.module.byteOrder };
            FrontendResult result = new(module, snapshot) { executionInputs = parsed.executionInputs };
            if (cached is not null && !module.diagnostics.Any(diagnostic => diagnostic.isFailure)
                && !new NativeModuleVerifier().Validate(module).Any(diagnostic => diagnostic.isFailure))
                frontendCache!.Capture(cached, source, result, linked.Token);
            return result;

            async ValueTask Replay(CancellationToken token)
            {
                // Includes successful and failed dynamic/absolute __has_include queries:
                // a dependency list alone cannot report every absence the compiler observed.
                if (!SourceSnapshotVerifier.Matches(view.inputs, token)) throw new IOException("Native source-view inputs changed before compiler observation replay.");
                string replay = Path.Combine(temporaryParent, "bgcs-native-source-view-replay-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(replay);
                try
                {
                    NativeSourceViewResult current = await new NativeSourceViewPreprocessor().PrepareAsync(new(source, replay)
                    { timeoutSeconds = checked((int)Math.Ceiling(timeout.TotalSeconds)) }, token).ConfigureAwait(false);
                    if (view.observationFingerprint != current.observationFingerprint)
                        throw new IOException("Native compiler source-view observations changed during generation; dynamic or absolute header queries and final macro state must remain identical.");
                    if (!SourceSnapshotVerifier.Matches(view.inputs, token) || !SourceSnapshotVerifier.Matches(current.inputs, token))
                        throw new IOException("Native source-view inputs changed during compiler observation replay.");
                }
                finally { Directory.Delete(replay, recursive: true); }
            }
        }
        catch (OperationCanceledException failure)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (deadline.IsCancellationRequested) throw new TimeoutException("The native source-view and parser deadline elapsed.", failure);
            throw;
        }
        finally { Directory.Delete(root, recursive: true); }

        bool Within(string path) => Path.GetFullPath(path).StartsWith(root + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static ImmutableArray<string> ParserArguments(ImmutableArray<string> arguments)
    {
        ImmutableArray<string>.Builder result = ImmutableArray.CreateBuilder<string>();
        for (int index = 0; index < arguments.Length; index++)
        {
            string argument = arguments[index];
            if (argument is "-D" or "-U" or "-include" or "-imacros") { index++; continue; }
            if (argument.StartsWith("-D", StringComparison.Ordinal) || argument.StartsWith("-U", StringComparison.Ordinal)
                || argument.StartsWith("-include", StringComparison.Ordinal) || argument.StartsWith("-imacros", StringComparison.Ordinal)) continue;
            result.Add(argument);
        }
        return result.ToImmutable();
    }
}
