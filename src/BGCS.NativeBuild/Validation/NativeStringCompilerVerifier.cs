using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Native;
using BGCS.Contracts.Requests;
using BGCS.Infrastructure.Execution;
using BGCS.Infrastructure.IO;
using BGCS.Contracts.Inputs;
using BGCS.NativeBuild.Planning;

namespace BGCS.NativeBuild.Validation;

/// <summary>Certifies literal code units with a headerless native C++ compiler invocation under the original execution-character options.</summary>
public sealed class NativeStringCompilerVerifier
{
    /// <summary>Checks actual expanded literals before the final native construction executes.</summary>
    /// <param name="plan">The frozen native compiler and target identity.</param>
    /// <param name="operation">The exact source preprocessing command supplying the expanded tokens.</param>
    /// <param name="expected">The constants used by generation.</param>
    /// <param name="actual">Facts extracted from the actual preprocessed original C or C++ source.</param>
    /// <param name="cancellationToken">The caller's shared native proof deadline and cancellation token.</param>
    /// <returns>The actual compiler proof receipt, or null when no selected native string exists.</returns>
    /// <remarks>This requires the selected compiler's C++ frontend even for original C headers. A missing frontend or incompatible literal form is an explicit failure.</remarks>
    public async ValueTask<NativeStringProofReceipt?> VerifyAsync(
        NativeBuildPlan plan,
        NativePreprocessingOperation operation,
        NativeModule expected,
        NativeModule actual,
        CancellationToken cancellationToken = default
    ) {
        string? source = new NativeStringAssertionEmitter().Emit(expected, actual);
        if (source is null) return null;
        string family = plan.compilerFamily ?? plan.providerId;
        if (family is not ("msvc" or "clang-cl" or "clang" or "gnu")) throw new NotSupportedException("The native provider has no execution-character literal proof implementation.");
        string root = plan.scratchDirectory ?? throw new InvalidOperationException("Native literal proof requires an owned native scratch directory.");
        string input = Path.Combine(root, "literal-proof-" + Guid.NewGuid().ToString("N") + ".cpp");
        try
        {
            await File.WriteAllTextAsync(input, source, cancellationToken).ConfigureAwait(false);
            SourceSnapshot snapshot = new ExternalInputSnapshotBuilder().Capture([new(input, ExternalInputKind.File)], root, cancellationToken);
            List<string> arguments = [];
            bool foundSource = false;
            NativeArgumentExpansionResult expanded = new NativeArgumentInputSnapshotBuilder().Expand(operation.command.arguments, operation.command.workingDirectory,
                windowsResponseSyntax: family is "msvc" or "clang-cl", clangConfigurations: family is "clang" or "clang-cl", compilerPath: operation.command.executable,
                cancellationToken: cancellationToken, snapshotExcludedDirectories: plan.snapshotExcludedDirectories.Add(root),
                includeSearchDirectories: plan.includeSearchDirectories, sysRoot: plan.target.toolchain.sysRoot);
            ImmutableArray<string> original = expanded.arguments;
            for (int index = 0; index < original.Length; index++)
            {
                string argument = original[index];
                if (PathEquals(argument, operation.source)) { foundSource = true; continue; }
                if (family is "msvc" or "clang-cl")
                {
                    if (argument is "/P" or "/EP" or "/E" or "/TC" or "/TP" or "/c" or "/Zs") continue;
                    if (argument is "/Fi" or "/FI" or "/Fo" or "/sourceDependencies") { index++; continue; }
                    if (argument.StartsWith("/Fi", StringComparison.Ordinal) || argument.StartsWith("/FI", StringComparison.Ordinal)
                        || argument.StartsWith("/Fo", StringComparison.Ordinal) || argument.StartsWith("/std:", StringComparison.Ordinal)) continue;
                    if (argument is "/clang:-MF" or "/clang:-include" or "/clang:-imacros" or "/clang:-include-pch") { index++; continue; }
                    if (argument is "/clang:-MD" or "/clang:-MMD" or "/clang:-M" or "/clang:-MM") continue;
                }
                else
                {
                    if (argument is "-o" or "-MF" or "-MT" or "-MQ" or "-x" or "-include" or "-imacros" or "-include-pch") { index++; continue; }
                    if (argument is "-E" or "-P" or "-M" or "-MM" or "-MD" or "-MMD" or "-MP" or "-c" or "-fsyntax-only"
                        || argument.StartsWith("-std=", StringComparison.Ordinal)) continue;
                }
                arguments.Add(argument);
            }
            if (!foundSource) throw new NotSupportedException("The native literal proof could not identify its original source argument.");
            arguments.AddRange(family is "msvc" or "clang-cl" ? ["/TP", "/std:c++latest", "/Zs"] : ["-x", "c++", "-std=c++23", "-fsyntax-only"]);
            arguments.Add(input);
            ProcessExecutionResult checkedLiterals = await ProcessExecutor.ExecuteAsync(operation.command.executable, arguments, operation.command.workingDirectory,
                TimeSpan.FromSeconds(300), cancellationToken, environmentOverrides: operation.command.environmentOverrides,
                outputEncoding: operation.command.outputEncoding is string encoding ? Encoding.GetEncoding(encoding) : null).ConfigureAwait(false);
            if (checkedLiterals.timedOut || checkedLiterals.exitCode != 0)
                throw new InvalidOperationException("Native execution-character string verification failed: " + checkedLiterals.standardOutput + Environment.NewLine + checkedLiterals.standardError);
            if (!SourceSnapshotVerifier.Matches(snapshot, cancellationToken) || !SourceSnapshotVerifier.Matches(expanded.snapshot, cancellationToken)
                || !SourceSnapshotVerifier.Matches(plan.argumentInputs, cancellationToken))
                throw new IOException("The exact native string proof input or original compiler argument resources changed during verification.");
            NativeBuildCommand proofCommand = new("native-literal-proof", operation.command.executable, arguments.ToImmutableArray(), operation.command.workingDirectory)
            { environmentOverrides = operation.command.environmentOverrides, outputEncoding = operation.command.outputEncoding, inputFiles = [input] };
            return new(proofCommand, source, snapshot.files.Single().digest,
                NativeStringAssertionEmitter.SelectConstants(expected).Select(declaration => declaration.id).ToImmutableArray(), expanded.snapshot, checkedLiterals);
        }
        finally { if (File.Exists(input)) File.Delete(input); }
    }

    private static bool PathEquals(
        string left,
        string right
    ) => string.Equals(left, right, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
