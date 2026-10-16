using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Requests;
using BGCS.Infrastructure.IO;

namespace BGCS.NativeBuild.Planning;

internal static class NativeResponseFileInputs
{
    internal static void RejectPinnedLinkerOverrides(
        NativeBuildRequest request,
        string family,
        string compilerPath,
        CancellationToken cancellationToken
    ) {
        string origin = request.argumentWorkingDirectory ?? request.workingDirectory;
        ImmutableArray<string> searches = request.includeDirectories.Concat(request.systemIncludeDirectories)
            .Concat(request.target.toolchain.systemIncludeFolders).Concat(request.target.toolchain.cxxSystemIncludeFolders).ToImmutableArray();
        foreach ((ImmutableArray<string> options, bool windows, bool configurations) in new[]
            { (request.compilerArguments, family is "msvc" or "clang-cl", family is "clang" or "clang-cl"),
              (request.linkerArguments, request.target.platformId == "windows", family == "clang" && request.target.platformId != "windows") })
        {
            NativeArgumentExpansionResult expanded = new NativeArgumentInputSnapshotBuilder().Expand(options, origin, windows,
                configurations, compilerPath, cancellationToken,
                request.snapshotExcludedDirectories.Append(request.workingDirectory), searches, request.target.toolchain.sysRoot);
            foreach (string original in expanded.arguments.Concat(expanded.linkerOnlyArguments))
            {
                string value = original.StartsWith("/clang:", StringComparison.Ordinal) ? original[7..] : original;
                if (value is "--ld-path" or "-fuse-ld" || value.StartsWith("--ld-path=", StringComparison.Ordinal) || value.StartsWith("-fuse-ld=", StringComparison.Ordinal))
                    throw new InvalidOperationException("Unstructured native arguments cannot replace the explicitly pinned linker; change the resolved toolchain descriptor.");
            }
        }
    }

    internal static SourceSnapshot CaptureRequest(
        NativeBuildRequest request,
        string family,
        CancellationToken cancellationToken,
        string? compilerPath = null
    ) {
        bool windows = family is "msvc" or "clang-cl";
        if (family is "clang" or "clang-cl")
            foreach (string option in request.compilerArguments)
            {
                if (option == "--rsp-quoting=windows") windows = true;
                else if (option == "--rsp-quoting=posix") windows = false;
            }
        string origin = request.argumentWorkingDirectory ?? request.workingDirectory;
        ImmutableArray<string> exclusions = request.snapshotExcludedDirectories.Append(request.workingDirectory).ToImmutableArray();
        ImmutableArray<string> searches = request.includeDirectories.Concat(request.systemIncludeDirectories).Concat(request.target.toolchain.systemIncludeFolders).Concat(request.target.toolchain.cxxSystemIncludeFolders).ToImmutableArray();
        NativeArgumentExpansionResult compileExpansion = new NativeArgumentInputSnapshotBuilder().Expand(request.compilerArguments, origin, windows, family is "clang" or "clang-cl", compilerPath, cancellationToken,
            exclusions, searches, request.target.toolchain.sysRoot);
        NativeArgumentIdentityValidator.Validate(compileExpansion.arguments.Concat(compileExpansion.linkerOnlyArguments), "native compiler");
        if (family is "msvc" or "clang-cl" && compileExpansion.arguments.Any(argument => argument.TrimStart('/', '-').Equals("Zc:preprocessor-", StringComparison.OrdinalIgnoreCase)))
            throw new NotSupportedException("BGCS native source views require the conformant MSVC preprocessor; /Zc:preprocessor- has no faithful supported source-view capability.");
        NativeArgumentExpansionResult linkExpansion = new NativeArgumentInputSnapshotBuilder().Expand(request.linkerArguments, origin, request.target.platformId == "windows",
            family == "clang" && request.target.platformId != "windows", compilerPath, cancellationToken, exclusions, searches, request.target.toolchain.sysRoot);
        NativeArgumentIdentityValidator.Validate(linkExpansion.arguments.Concat(linkExpansion.linkerOnlyArguments), "native linker");
        SourceSnapshot compile = compileExpansion.snapshot, link = linkExpansion.snapshot;
        return new(compile.files.Concat(link.files).DistinctBy(input => input.path, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToImmutableArray(),
            compile.searchDirectories.Concat(link.searchDirectories).DistinctBy(input => input.path, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToImmutableArray())
        {
            absentInputs = compile.absentInputs.Concat(link.absentInputs).DistinctBy(input => input.path, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToImmutableArray(),
            excludedDirectories = compile.excludedDirectories.Concat(link.excludedDirectories).Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToImmutableArray()
        };
    }
}
