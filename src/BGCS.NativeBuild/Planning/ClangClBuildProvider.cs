using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;
using BGCS.Platforms.Toolchains;

namespace BGCS.NativeBuild.Planning;

/// <summary>Plans clang-cl compilation with explicitly pinned Visual C++ headers and linker paths.</summary>
public sealed class ClangClBuildProvider : INativeBuildProvider, INativeBuildPrerequisiteProvider
{
    private readonly string m_compiler;
    private readonly MsvcBuildProvider m_msvc;
    /// <summary>Composes clang-cl with the complete Visual C++ SDK selected for the target.</summary>
    /// <param name="compiler">The absolute installed clang-cl executable.</param>
    /// <param name="sdk">The target-specific installed Visual C++ and Windows SDK paths.</param>
    /// <param name="archiver">An optional authoritative MSVC-compatible archive tool.</param>
    public ClangClBuildProvider(
        string compiler,
        MsvcToolchainInfo sdk,
        string? archiver = null
    ) {
        if (!Path.IsPathFullyQualified(compiler) || !File.Exists(compiler)) throw new FileNotFoundException("clang-cl must exist at an absolute path.", compiler);
        m_compiler = compiler;
        m_msvc = new(sdk, archiver);
    }
    /// <inheritdoc/>
    public string id => "clang-cl";
    /// <inheritdoc/>
    public ImmutableArray<NativeToolPrerequisite> DescribePrerequisites() => m_msvc.DescribePrerequisites()
        .Select(tool => tool.role == "compiler" ? tool with { id = id, path = m_compiler } : tool).ToImmutableArray();
    /// <inheritdoc/>
    public NativeBuildPlan Plan(
        NativeBuildRequest request,
        CancellationToken cancellationToken
    ) {
        // MSVC's command grammar and linker remain explicit; the C/C++ standard belongs to Clang.
        NativeBuildRequest baseline = request with { sources = request.sources.Select(unit => unit with { standard = unit.language == SourceLanguage.C ? "c17" : "c++20" }).ToImmutableArray(), preprocessingSources = [], compilerArguments = [], defines = [] };
        NativeBuildPlan direct = m_msvc.Plan(baseline, cancellationToken);
        ImmutableArray<NativeBuildCommand>.Builder commands = ImmutableArray.CreateBuilder<NativeBuildCommand>();
        for (int index = 0; index < direct.commands.Length; index++)
        {
            NativeBuildCommand command = direct.commands[index];
            if (index < request.sources.Length)
            {
                NativeCompilationUnit unit = request.sources[index];
                if (unit.standard is not ("c11" or "c17" or "c23" or "c++14" or "c++17" or "c++20" or "c++23"))
                    throw new NotSupportedException("The requested standard is not available through clang-cl.");
                ImmutableArray<string> arguments = ConvertArguments(command.arguments, unit.standard, direct.dependencyFiles[index].path, scan: false);
                commands.Add(command with { executable = m_compiler, arguments = arguments });
            }
            else commands.Add(command);
        }
        ImmutableArray<NativeDependencyScan> scans = direct.dependencyScans.Select((
            scan,
            index
        ) => scan with
        {
            command = scan.command with { executable = m_compiler, arguments = ConvertArguments(scan.command.arguments, request.sources[index].standard, scan.dependencyFile.path, scan: true) },
            dependencyFile = scan.dependencyFile with { format = NativeDependencyFormat.Make }
        }).ToImmutableArray();
        return NativePreprocessingPlanner.AppendSources(direct with
        {
            providerId = id,
            commands = commands.ToImmutable(),
            compilerFamily = id,
            compilerPath = m_compiler,
            argumentInputs = NativeResponseFileInputs.CaptureRequest(request, id, cancellationToken, m_compiler),
            dependencyScans = scans,
            dependencyFiles = direct.dependencyFiles.Select(file => file with { format = NativeDependencyFormat.Make }).ToImmutableArray(),
            preprocessingOperations = NativePreprocessingPlanner.Create(request, id, scans),
            externalInputFiles = direct.externalInputFiles.Append(m_compiler).Distinct(StringComparer.OrdinalIgnoreCase).ToImmutableArray()
        }, request);

        ImmutableArray<string> ConvertArguments(
            ImmutableArray<string> original,
            string standard,
            string dependencies,
            bool scan
        ) {
            List<string> result = ["--no-default-config", "--target=" + request.target.triple];
            bool insertedUserArguments = false;
            for (int argument = 0; argument < original.Length; argument++)
            {
                string value = original[argument];
                if (value == "/sourceDependencies") { argument++; continue; }
                if (value.StartsWith("/std:", StringComparison.Ordinal) || value == "/Zs" || scan && value == "/c") continue;
                if (!insertedUserArguments && request.sources.Any(unit => unit.path == value))
                {
                    result.AddRange(request.defines.Select(define => "/D" + define));
                    result.AddRange(request.compilerArguments);
                    insertedUserArguments = true;
                }
                result.Add(value);
            }
            if (!insertedUserArguments) throw new InvalidOperationException("The clang-cl adapter did not identify its exact native source input.");
            result.Add("/clang:-std=" + standard);
            result.AddRange(request.target.toolchain.arguments.Where(argument => argument.StartsWith("-fms-compatibility-version=", StringComparison.Ordinal)).Select(argument => "/clang:" + argument));
            result.AddRange([scan ? "/clang:-M" : "/clang:-MD", "/clang:-MF", "/clang:" + dependencies]);
            return result.ToImmutableArray();
        }
    }
}
