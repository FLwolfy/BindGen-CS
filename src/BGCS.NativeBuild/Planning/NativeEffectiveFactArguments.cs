using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using BGCS.Contracts.Requests;
using BGCS.Infrastructure.IO;

namespace BGCS.NativeBuild.Planning;

/// <summary>Translates supported effective native layout options into an independent frontend verification request.</summary>
public static class NativeEffectiveFactArguments
{
    /// <summary>Freezes response expansion again and rejects native AST mutations that cannot be independently represented.</summary>
    /// <param name="plan">The already locked native construction plan.</param>
    /// <param name="operation">The exact native preprocessing command.</param>
    /// <param name="cancellationToken">Cancels response-file hashing.</param>
    /// <returns>Individual frontend options for the native compiler's explicitly selected layout and scalar semantics.</returns>
    public static ImmutableArray<string> Resolve(
        NativeBuildPlan plan,
        NativePreprocessingOperation operation,
        CancellationToken cancellationToken = default
    ) {
        string family = plan.compilerFamily ?? plan.providerId;
        NativeArgumentExpansionResult expanded = new NativeArgumentInputSnapshotBuilder().Expand(operation.command.arguments,
            operation.command.workingDirectory, windowsResponseSyntax: family is "msvc" or "clang-cl",
            clangConfigurations: family is "clang" or "clang-cl", compilerPath: operation.command.executable,
            cancellationToken: cancellationToken, snapshotExcludedDirectories: plan.snapshotExcludedDirectories.AddRange(plan.scratchDirectory is string scratch ? ImmutableArray.Create(scratch) : ImmutableArray<string>.Empty),
            includeSearchDirectories: plan.includeSearchDirectories, sysRoot: plan.target.toolchain.sysRoot);
        string ownedTarget = "--target=" + plan.target.triple;
        int trustedTargets = expanded.arguments.Count(argument => argument == ownedTarget);
        if (family is "clang" or "clang-cl" && trustedTargets != 1)
            throw new InvalidOperationException("The native preprocessing operation requires exactly one provider-authored frozen target option.");
        if (family is not ("clang" or "clang-cl") && trustedTargets != 0)
            throw new InvalidOperationException("This native compiler family has no provider-authored explicit target override.");
        // Planning already rejected target overrides in unstructured caller arguments.
        // Only the one exact option authored by the frozen Clang provider is trusted here.
        NativeArgumentIdentityValidator.Validate(expanded.arguments.Where(argument => argument != ownedTarget), "native effective source");
        if (!SourceSnapshotVerifier.Matches(plan.argumentInputs, cancellationToken))
            throw new System.IO.IOException("Native argument inputs changed before effective source verification.");
        List<string> result = [];
        foreach (string original in expanded.arguments)
        {
            string argument = original.StartsWith("/clang:", StringComparison.OrdinalIgnoreCase) ? original[7..] : original;
            if (argument is "-funsigned-char" or "-fsigned-char" or "-fno-unsigned-char" or "-fno-signed-char"
                or "-fshort-enums" or "-fno-short-enums" or "-mms-bitfields" or "-mno-ms-bitfields"
                or "-funsigned-bitfields" or "-fsigned-bitfields"
                or "-fms-extensions" or "-fno-ms-extensions" or "-fms-compatibility" or "-fno-ms-compatibility"
                or "-fpack-struct" or "-fno-pack-struct" or "-fshort-wchar" or "-fno-short-wchar"
                || argument.StartsWith("-fpack-struct=", StringComparison.Ordinal) || argument.StartsWith("-fms-compatibility-version=", StringComparison.Ordinal)
                || argument.StartsWith("-fclang-abi-compat=", StringComparison.Ordinal)) result.Add(argument);
            else if (family is "msvc" or "clang-cl" && argument == "/J") result.Add("-funsigned-char");
            else if (family is "msvc" or "clang-cl" && argument.StartsWith("/Zp", StringComparison.Ordinal))
            {
                string packing = argument[3..];
                if (packing is not ("1" or "2" or "4" or "8" or "16"))
                    throw new NotSupportedException("Effective native packing verification requires an explicit /Zp1, /Zp2, /Zp4, /Zp8 or /Zp16.");
                result.Add("-fpack-struct=" + packing);
            }
            else if (argument is "-load" or "-plugin" or "-add-plugin" || argument.StartsWith("-fplugin", StringComparison.Ordinal)
                || argument.StartsWith("-fabi-version", StringComparison.Ordinal) || argument.StartsWith("-fabi-compat-version", StringComparison.Ordinal)
                || family is "msvc" or "clang-cl" && (argument.StartsWith("/vd", StringComparison.Ordinal) || argument.StartsWith("/vm", StringComparison.Ordinal)))
                throw new NotSupportedException("The effective native declaration transform has no independent proof implementation: " + argument);
        }
        return result.ToImmutableArray();
    }
}
