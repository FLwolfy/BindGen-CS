using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using BGCS.Contracts.Inputs;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Native;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;
using BGCS.Frontend.Clang.Facts;
using BGCS.Frontend.Clang.Parsing;
using BGCS.Frontend.Clang.Interop;
using BGCS.Infrastructure.IO;
using BGCS.Frontend.Clang.Advanced;

namespace BGCS.Frontend.Clang;

/// <summary>Extracts detached native facts while owning the complete native parse lifetime.</summary>
public sealed class ClangFrontend : IFrontend
{
    /// <inheritdoc/>
    public ValueTask<FrontendResult> ParseAsync(
        FrontendRequest request,
        CancellationToken cancellationToken
    ) => ParseAsync(request, [], cancellationToken);

    /// <summary>Runs explicitly supplied advanced fact contributors while the Clang translation unit remains alive.</summary>
    /// <param name="request">Frozen native parser inputs.</param>
    /// <param name="contributors">Ordered process-local contributors; normal SDK generation activates these only inside a worker.</param>
    /// <param name="cancellationToken">Cancels parsing and cooperative contribution.</param>
    /// <returns>Detached facts and verified parser dependencies.</returns>
    public ValueTask<FrontendResult> ParseAsync(
        FrontendRequest request,
        IReadOnlyList<IClangFactContributor> contributors,
        CancellationToken cancellationToken
    ) {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(contributors);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.headers.IsDefaultOrEmpty)
            throw new ArgumentException("At least one entry header is required.", nameof(request));
        if (request.language is not (SourceLanguage.C or SourceLanguage.Cpp))
            throw new NotSupportedException("Only C and C++ declaration inputs are supported.");
        FrontendArgumentValidator.Validate(request);
        string resourceDirectory = ClangResourceHeaders.VerifyDirectory();
        SourceSnapshot executionInputs = new ExternalInputSnapshotBuilder().Capture(
            [new(resourceDirectory, ExternalInputKind.Directory)], resourceDirectory, cancellationToken);
        CppParserOptions options = new()
        {
            parserKind = request.language,
            parseMacros = request.parseMacros,
            parseComments = request.parseComments,
            retainFunctionBodies = request.retainFunctionBodies,
            parseSystemIncludes = request.parseSystemIncludes,
            autoSquashTypedef = false,
            preHeaderText = request.preamble,
            postHeaderText = request.postamble,
            sourcePathMappings = request.sourcePathMappings,
            usePresumedSourceLocations = request.usePresumedSourceLocations
        };
        options.ConfigureForTarget(request.target, request.headerSearchPaths);
        options.includeFolders.AddRange(request.includeDirectories);
        options.systemIncludeFolders.AddRange(request.systemIncludeDirectories);
        options.defines.AddRange(request.defines);
        options.additionalArguments.AddRange(request.arguments);
        options.additionalArguments.Add("-std=" + request.languageStandard);
        string[] searchRoots = request.headers.Select(header => Path.GetDirectoryName(Path.GetFullPath(header))!)
            .Concat(request.includeDirectories).Concat(request.systemIncludeDirectories)
            .Concat(request.target.toolchain.cxxSystemIncludeFolders).Concat(request.target.toolchain.systemIncludeFolders)
            .Concat(request.headerSearchPaths.Where(path => !path.isBuiltin).Select(path => path.path))
            .Append(Path.Combine(resourceDirectory, "include")).Distinct(StringComparer.Ordinal).ToArray();
        ImmutableArray<DirectoryInputDependency> directories = searchRoots.Select(path => DirectoryMembershipDigest.Capture(path, request.snapshotExcludedDirectories, cancellationToken)).ToImmutableArray();
        using var compilation = CppParser.ParseFiles(request.headers.ToList(), options);
        cancellationToken.ThrowIfCancellationRequested();
        NativeModuleBuilder builder = new(request, compilation);
        NativeModule module = builder.Build(compilation);
        foreach (IClangFactContributor contributor in contributors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(contributor);
            ClangFactContributionContext context = builder.CreateContributionContext(compilation.translationUnit);
            NativeModule original = module;
            try { module = contributor.Contribute(context, original, cancellationToken) ?? throw new InvalidOperationException("A Clang fact contributor returned no detached native module."); }
            finally { context.Invalidate(); }
            if (module.targetId != original.targetId || module.triple != original.triple || module.pointerSize != original.pointerSize
                || module.language != original.language || module.byteOrder != original.byteOrder)
                throw new InvalidOperationException("A Clang fact contributor changed the resolved native environment identity.");
        }
        ImmutableArray<InputDependency> files = IncludeDependencyCollector.Collect(compilation);
        if (!directories.SequenceEqual(searchRoots.Select(path => DirectoryMembershipDigest.Capture(path, request.snapshotExcludedDirectories, cancellationToken))))
            throw new IOException("Include-search membership changed while parsing.");
        ClangResourceHeaders.VerifyDirectory();
        if (!SourceSnapshotVerifier.Matches(executionInputs, cancellationToken))
            throw new IOException("Locked Clang parser resources changed while parsing.");
        return ValueTask.FromResult(new FrontendResult(module, new SourceSnapshot(files, directories) { excludedDirectories = request.snapshotExcludedDirectories })
        { executionInputs = executionInputs });
    }
}
