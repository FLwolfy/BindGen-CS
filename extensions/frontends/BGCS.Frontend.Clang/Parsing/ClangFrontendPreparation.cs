using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using BGCS.Core.Hosting;
using BGCS.Core.Inputs;
using BGCS.Core.Requests;
using BGCS.Core.Targets;
using BGCS.Frontend.Clang.Parsing.Arguments;

namespace BGCS.Frontend.Clang.Parsing;

internal static class ClangFrontendPreparation
{
    internal static FrontendPreparation Prepare(
        FrontendRequest request,
        HostServices host,
        CancellationToken cancellationToken
    ) {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(host);
        NativeTargetDescriptor target = request.target;
        NativeArgumentExpansionResult expanded = new NativeArgumentInputSnapshotBuilder(host.paths).Expand(
            target.toolchain.arguments.Concat(request.arguments), request.pathBase,
            windowsResponseSyntax: host.execution.hostRuntimeIdentifier.StartsWith("win-", StringComparison.Ordinal),
            clangConfigurations: true, compilerPath: target.toolchain.compilerPath, cancellationToken: cancellationToken,
            snapshotExcludedDirectories: request.snapshotExcludedDirectories,
            includeSearchDirectories: request.includeDirectories.Concat(request.systemIncludeDirectories)
                .Concat(target.toolchain.cxxSystemIncludeFolders).Concat(target.toolchain.systemIncludeFolders)
                .Concat(request.headerSearchPaths.Select(entry => entry.path)), sysRoot: target.toolchain.sysRoot);
        NativeArgumentIdentityValidator.Validate(expanded.arguments.Concat(expanded.linkerOnlyArguments), "Clang frontend", rejectParserSubstitution: true);
        NativeToolchainDescriptor inputs = new(target.toolchain.compilerPath, target.toolchain.sysRoot,
            target.toolchain.systemIncludeFolders, target.toolchain.defines, [], target.toolchain.cxxSystemIncludeFolders,
            target.toolchain.sdkVersion, target.toolchain.compilerVersion);
        NativeTargetDescriptor detached = new(target.targetId, target.platformId, target.architectureId, target.abiId,
            target.triple, inputs, target.capabilities, target.byteOrder)
        { headerSearchPaths = target.headerSearchPaths, sourceConventions = target.sourceConventions };
        FrontendRequest prepared = request with { target = detached, arguments = expanded.arguments };
        return new(prepared, expanded.snapshot, expanded.cacheIneligibilityReasons.AddRange(CacheReasons(prepared, [], cancellationToken)).Distinct(StringComparer.Ordinal).ToImmutableArray());
    }

    internal static ImmutableArray<string> CacheReasons(
        FrontendRequest request,
        IEnumerable<InputDependency> files,
        CancellationToken cancellationToken
    ) {
        ImmutableArray<string>.Builder reasons = ImmutableArray.CreateBuilder<string>();
        if (request.arguments.Any(argument => argument.Contains("pch", StringComparison.OrdinalIgnoreCase)
            || argument.Contains("module", StringComparison.OrdinalIgnoreCase) || argument.Contains("vfs", StringComparison.OrdinalIgnoreCase)
            || argument.Contains("profile", StringComparison.OrdinalIgnoreCase)))
            reasons.Add("The Clang frontend cannot establish complete reusable precompiled, modular, virtual-filesystem or profile inputs.");
        if (Volatile(request.preamble) || Volatile(request.postamble)
            || request.defines.Concat(request.target.toolchain.defines).Concat(request.arguments).Any(Volatile))
            reasons.Add("Volatile or token-pasted preprocessing inputs require a fresh parse.");
        foreach (InputDependency input in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (input.length > 67108864) { reasons.Add("A Clang source exceeds the bounded volatility inspection limit."); continue; }
            if (Volatile(File.ReadAllText(input.path, Encoding.UTF8)))
                reasons.Add("Volatile or token-pasted preprocessing inputs require a fresh parse: " + input.path);
        }
        return reasons.ToImmutable();
    }

    private static bool Volatile(string value)
    {
        value = value.Replace("\\\r\n", "", StringComparison.Ordinal).Replace("\\\n", "", StringComparison.Ordinal);
        return value.Contains("__DATE__", StringComparison.Ordinal) || value.Contains("__TIME__", StringComparison.Ordinal)
            || value.Contains("__TIMESTAMP__", StringComparison.Ordinal) || value.Contains("__has_include", StringComparison.Ordinal)
            || value.Contains("##", StringComparison.Ordinal) || value.Contains("%:%:", StringComparison.Ordinal);
    }
}
