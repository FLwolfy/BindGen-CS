using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Requests;
using BGCS.NativeBuild.Planning;
using BGCS.NativeBuild.Execution;
using BGCS.Platforms.Targeting;

namespace BGCS.NativeBuild.Validation;

/// <summary>Preserves native preprocessing tokens while representing compiler-specific diagnostic metadata and unused dependent intrinsics.</summary>
public static class NativeEffectiveSourceAdapter
{
    /// <summary>Prepares an independently parsed source input without changing any native type-trait result.</summary>
    /// <param name="plan">The frozen native construction plan.</param>
    /// <param name="operation">The completed native preprocessing operation.</param>
    /// <param name="cancellationToken">Cancels reading or writing the private source input.</param>
    /// <returns>The preserved source path, parser-only preamble and optional isolated dependency declarations; unsupported concrete intrinsic evaluations still fail.</returns>
    public static async ValueTask<(string sourceFile, string preamble, NativeDeclarationPrelude? declarationPrelude)> PrepareAsync(
        NativeBuildPlan plan,
        NativePreprocessingOperation operation,
        CancellationToken cancellationToken = default
    ) {
        string family = plan.compilerFamily ?? plan.providerId;
        if (family == "gnu" && operation.language == SourceLanguage.Cpp)
        {
            string trace = await File.ReadAllTextAsync(operation.outputFile, cancellationToken).ConfigureAwait(false);
            var roots = ToolchainDiscovery.DiscoverHeaderSearchPaths(operation.language, plan.compilerPath ?? operation.command.executable, plan.target.toolchain.sysRoot)
                .Select(entry => entry.path).ToImmutableArray();
            var dependencies = BuildDependencyCollector.Read([operation.dependencyFile ?? throw new InvalidOperationException("GNU source adaptation requires actual preprocessing dependencies.")],
                operation.command.workingDirectory, cancellationToken).ToImmutableArray();
            var extracted = NativeGnuDeclarationPrelude.Extract(trace, roots, dependencies, operation.source, operation.command.workingDirectory, cancellationToken);
            string adaptedDestination = operation.outputFile + ".facts.hpp";
            await File.WriteAllTextAsync(adaptedDestination, extracted.source, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            return (adaptedDestination, string.Empty, extracted.prelude);
        }
        if (family != "msvc") return (operation.outputFile, string.Empty, null);
        string scratch = Path.GetFullPath(plan.scratchDirectory ?? throw new InvalidOperationException("MSVC effective facts require an owned scratch directory."));
        string original = Path.GetFullPath(operation.outputFile);
        if (!original.StartsWith(scratch.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("MSVC effective fact input must remain inside the owned native scratch directory.");
        string source = await File.ReadAllTextAsync(original, cancellationToken).ConfigureAwait(false);
        string adapted = PreserveSystemHeaderMarkers(source, plan.target.toolchain.systemIncludeFolders.Concat(plan.target.toolchain.cxxSystemIncludeFolders).ToImmutableArray());
        string destination = original + ".facts." + (operation.language == SourceLanguage.Cpp ? "hpp" : "h");
        await File.WriteAllTextAsync(destination, adapted, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        return (destination, ParserPreamble("msvc", operation.language), null);
    }

    /// <summary>Provides parser-only declarations that reject every unsupported concrete native intrinsic evaluation.</summary>
    /// <param name="compilerFamily">The native compiler family that produced the source tokens.</param>
    /// <param name="language">The native source language.</param>
    /// <returns>A guarded dependent-intrinsic preamble, or an empty string when no compatibility declarations are needed.</returns>
    public static string ParserPreamble(
        string compilerFamily,
        SourceLanguage language
    )
        => compilerFamily == "msvc" && language == SourceLanguage.Cpp ? MsvcDependentIntrinsicPreamble : string.Empty;

    /// <summary>Translates MSVC line-directive metadata to equivalent Clang system-header markers, preserving every language token.</summary>
    /// <param name="source">The exact native compiler-preprocessed text.</param>
    /// <param name="systemDirectories">The resolved native SDK include roots.</param>
    /// <returns>Text with unchanged line count and source tokens.</returns>
    public static string PreserveSystemHeaderMarkers(
        string source,
        ImmutableArray<string> systemDirectories
    ) {
        string[] roots = systemDirectories.Select(path => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar).Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToArray();
        return NativeLineDirectiveReader.Rewrite(source, directive =>
        {
            if (!directive.msvc) return null;
            string encoded = source.Substring(directive.fileStart - 1, directive.fileLength + 2);
            string file = directive.file;
            bool system = Path.IsPathFullyQualified(file) && roots.Any(root => Path.GetFullPath(file).StartsWith(root,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
            return "# " + directive.lineNumber + " " + encoded + (system ? " 3" : string.Empty);
        });
    }

    // Microsoft headers can mention intrinsics in uninstantiated dependent templates
    // that Clang does not implement. No substitute value is usable: instantiating any
    // unavailable trait or member operation produces a hard diagnostic, including in
    // SFINAE. The verifier rejects every parse containing an error. Known Clang
    // implementations remain untouched; new unknown intrinsics remain parse failures.
    private const string MsvcDependentIntrinsicPreamble = """
        namespace bgcs_effective_native_intrinsics_v1 {
        template<class...> inline constexpr bool unavailable = false;
        template<class... Types> struct rejected_trait {
            static_assert(unavailable<Types...>, "BGCS effective source requires an unsupported native intrinsic");
            static constexpr bool value = false;
        };
        template<class... Types, class... Values> constexpr bool rejected_member(Values...) {
            static_assert(unavailable<Types...>, "BGCS effective source requires an unsupported native member intrinsic");
            return false;
        }
        }
        #define __builtin_assume_aligned bgcs_effective_native_assume_aligned_v1
        #if !__has_builtin(__is_assignable_no_precondition_check)
        #define __is_assignable_no_precondition_check(...) (::bgcs_effective_native_intrinsics_v1::rejected_trait<__VA_ARGS__>::value)
        #endif
        #if !__has_builtin(__is_layout_compatible)
        #define __is_layout_compatible(...) (::bgcs_effective_native_intrinsics_v1::rejected_trait<__VA_ARGS__>::value)
        #endif
        #if !__has_builtin(__is_pointer_interconvertible_base_of)
        #define __is_pointer_interconvertible_base_of(...) (::bgcs_effective_native_intrinsics_v1::rejected_trait<__VA_ARGS__>::value)
        #endif
        #if !__has_builtin(__is_pointer_interconvertible_with_class)
        #define __is_pointer_interconvertible_with_class(T, value) (::bgcs_effective_native_intrinsics_v1::rejected_member<T>(value))
        #endif
        #if !__has_builtin(__is_corresponding_member)
        #define __is_corresponding_member(T, U, value, other) (::bgcs_effective_native_intrinsics_v1::rejected_member<T, U>(value, other))
        #endif
        """;
}
