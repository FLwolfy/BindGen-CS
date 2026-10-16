using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Linq;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;
using BGCS.Platforms.Toolchains;

namespace BGCS.NativeBuild.Planning;

/// <summary>Plans target-specific MSVC compilation and direct linking with explicit SDK paths.</summary>
public sealed class MsvcBuildProvider : INativeBuildProvider, INativeBuildPrerequisiteProvider
{
    private readonly MsvcToolchainInfo m_toolchain;
    private readonly string m_archiver;
    /// <summary>Captures an installed compiler, matching linker and complete SDK paths.</summary>
    /// <param name="toolchain">The immutable installed toolchain facts.</param>
    /// <param name="archiver">An optional authoritative archive tool, or null for the matching Visual C++ lib.exe.</param>
    public MsvcBuildProvider(
        MsvcToolchainInfo toolchain,
        string? archiver = null
    ) {
        m_toolchain = toolchain ?? throw new ArgumentNullException(nameof(toolchain));
        if (archiver is not null && (!Path.IsPathFullyQualified(archiver) || !File.Exists(archiver))) throw new FileNotFoundException("The explicit MSVC archive tool is unavailable.", archiver);
        m_archiver = archiver ?? Path.Combine(Path.GetDirectoryName(m_toolchain.linker)!, "lib.exe");
    }
    /// <inheritdoc/>
    public string id => "msvc";
    /// <inheritdoc/>
    public ImmutableArray<NativeToolPrerequisite> DescribePrerequisites() =>
        [new("compiler", "msvc", m_toolchain.compiler), new("linker", "link", m_toolchain.linker, NativeOutputKind.SharedLibrary), new("archive", "lib", m_archiver, NativeOutputKind.StaticLibrary)];
    /// <inheritdoc/>
    public NativeBuildPlan Plan(
        NativeBuildRequest request,
        CancellationToken cancellationToken
    ) {
        ArgumentNullException.ThrowIfNull(request);
        string argumentDirectory = NativeBuildEnvironment.ArgumentDirectory(request);
        if (!Enum.IsDefined(request.outputKind)) throw new ArgumentOutOfRangeException(nameof(request));
        if (request.outputKind == NativeOutputKind.Object && request.sources.Length != 1) throw new NotSupportedException("MSVC object output requires one compilation unit; use an archive for multiple units.");
        if (request.target.platformId != "windows" || request.target.abiId != "msvc") throw new NotSupportedException("MSVC DLL construction requires a Windows MSVC target.");
        if (request.sources.IsDefaultOrEmpty) throw new ArgumentException("At least one native compilation unit is required.", nameof(request));
        if (Path.GetFileName(Path.GetDirectoryName(m_toolchain.compiler))?.Equals(request.target.architectureId, StringComparison.OrdinalIgnoreCase) != true)
            throw new InvalidOperationException("The selected MSVC driver belongs to a different target architecture.");
        ImmutableArray<NativeBuildCommand>.Builder commands = ImmutableArray.CreateBuilder<NativeBuildCommand>();
        ImmutableArray<NativeDependencyScan>.Builder scans = ImmutableArray.CreateBuilder<NativeDependencyScan>();
        ImmutableArray<NativeDependencyFile>.Builder dependencies = ImmutableArray.CreateBuilder<NativeDependencyFile>();
        ImmutableArray<NativeLinkDependencyScan>.Builder linkScans = ImmutableArray.CreateBuilder<NativeLinkDependencyScan>();
        List<string> objects = [];
        for (int index = 0; index < request.sources.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NativeCompilationUnit unit = request.sources[index];
            if (!Path.IsPathFullyQualified(unit.path) || !File.Exists(unit.path)) throw new FileNotFoundException("A native source must exist at an absolute path.", unit.path);
            string suffix = index.ToString(CultureInfo.InvariantCulture);
            string output = request.outputKind == NativeOutputKind.Object ? request.outputFile : Path.Combine(request.workingDirectory, "unit-" + suffix + ".obj");
            // Keep source and execution defaults separate so explicit caller
            // charset options override one domain without MSVC D8016 conflicts.
            List<string> arguments = ["/nologo", "/c", "/MD", "/source-charset:utf-8", "/execution-charset:utf-8", "/W4", "/Zc:preprocessor", Standard(unit), unit.language == SourceLanguage.C ? "/TC" : "/TP"];
            if (unit.language == SourceLanguage.Cpp) arguments.AddRange(["/EHsc", "/Zc:__cplusplus", "/permissive-"]);
            foreach (string directory in request.includeDirectories) arguments.Add("/I" + directory);
            foreach (string directory in request.systemIncludeDirectories) arguments.Add("/external:I" + directory);
            foreach (string directory in m_toolchain.includeDirectories) arguments.Add("/external:I" + directory);
            arguments.Add("/external:W0");
            // MSVC expands percent placeholders inside /D values independently of shell parsing.
            // Grouped definitions describe literal macro text; raw compilerArguments retain driver syntax.
            foreach (string define in request.defines) arguments.Add("/D" + define.Replace("%", "%%", StringComparison.Ordinal));
            arguments.AddRange(request.compilerArguments);
            string dependency = Path.Combine(request.workingDirectory, "unit-" + suffix + ".deps.json");
            string scanDependency = Path.Combine(request.workingDirectory, "unit-" + suffix + ".scan.json");
            List<string> scanArguments = arguments.Where(argument => argument != "/c").Append("/Zs").Concat(new[] { unit.path, "/sourceDependencies", scanDependency }).ToList();
            scans.Add(new(new("dependencies-" + suffix, m_toolchain.compiler, scanArguments.ToImmutableArray(), argumentDirectory) { inputFiles = [unit.path], outputFiles = [scanDependency], environmentOverrides = NativeBuildEnvironment.cleanCompiler },
                new(scanDependency, NativeDependencyFormat.MsvcJson, unit.path) { workingDirectory = argumentDirectory }));
            arguments.AddRange(["/sourceDependencies", dependency]);
            dependencies.Add(new(dependency, NativeDependencyFormat.MsvcJson, unit.path) { workingDirectory = argumentDirectory });
            arguments.AddRange([unit.path, "/Fo" + output, "/Fd" + Path.Combine(request.workingDirectory, "unit-" + suffix + ".pdb")]);
            commands.Add(new("compile-" + suffix, m_toolchain.compiler, arguments.ToImmutableArray(), argumentDirectory) { inputFiles = [unit.path], outputFiles = [output], environmentOverrides = NativeBuildEnvironment.cleanCompiler });
            objects.Add(output);
        }
        if (request.outputKind == NativeOutputKind.Object)
            return Finish();
        bool archive = request.outputKind == NativeOutputKind.StaticLibrary;
        if (archive && (!request.libraries.IsEmpty || !request.libraryDirectories.IsEmpty || !request.linkerArguments.IsEmpty))
            throw new NotSupportedException("Static libraries preserve external dependencies for the consumer; they cannot absorb linker libraries or flags.");
        List<string> link = archive ? ["/NOLOGO", "/MACHINE:" + request.target.architectureId.ToUpperInvariant(), "/OUT:" + request.outputFile]
            : ["/NOLOGO", "/DLL", "/MACHINE:" + request.target.architectureId.ToUpperInvariant(), "/OUT:" + request.outputFile,
            "/IMPLIB:" + Path.Combine(request.workingDirectory, "native-import.lib")];
        link.AddRange(objects);
        if (!archive)
        {
            foreach (string directory in m_toolchain.libraryDirectories) link.Add("/LIBPATH:" + directory);
            foreach (string directory in request.libraryDirectories) link.Add("/LIBPATH:" + directory);
        }
        foreach (string library in request.libraries)
        {
            if (Path.IsPathRooted(library)) link.Add(library);
            else if (library.Contains('/') || library.Contains('\\')) throw new ArgumentException("Native dependency file paths must be absolute.");
            else link.Add(Path.HasExtension(library) ? library : library + ".lib");
        }
        link.AddRange(request.linkerArguments);
        if (!archive) link.Add("/VERBOSE:LIB");
        string linker = archive ? m_archiver : m_toolchain.linker;
        if (!File.Exists(linker)) throw new FileNotFoundException("The selected MSVC archive tool is unavailable.", linker);
        commands.Add(new(archive ? "archive" : "link", linker, link.ToImmutableArray(), argumentDirectory)
        {
            inputFiles = objects.ToImmutableArray(),
            outputFiles = [request.outputFile],
            environmentOverrides = NativeBuildEnvironment.cleanCompiler,
            linkDependencyTrace = archive ? null : NativeLinkTraceFormat.MsvcLibrarySearch
        });
        if (!archive)
        {
            string probe = Path.Combine(request.workingDirectory, "native-link-probe.dll");
            ImmutableArray<string> probeArguments = link.Select(argument => argument.StartsWith("/OUT:", StringComparison.OrdinalIgnoreCase) ? "/OUT:" + probe
                : argument.StartsWith("/IMPLIB:", StringComparison.OrdinalIgnoreCase) ? "/IMPLIB:" + Path.Combine(request.workingDirectory, "native-link-probe.lib")
                : argument.StartsWith("/PDB:", StringComparison.OrdinalIgnoreCase) ? "/PDB:" + Path.Combine(request.workingDirectory, "native-link-probe.pdb") : argument).ToImmutableArray();
            linkScans.Add(new("link", new("dependencies-link", linker, probeArguments, argumentDirectory) { inputFiles = objects.ToImmutableArray(), outputFiles = [probe], environmentOverrides = NativeBuildEnvironment.cleanCompiler }, NativeLinkTraceFormat.MsvcLibrarySearch));
        }
        return Finish();

        NativeBuildPlan Finish() => NativePreprocessingPlanner.AppendSources(new(id, request.target, commands.ToImmutable(), request.outputFile)
        {
            argumentInputs = NativeResponseFileInputs.CaptureRequest(request, id, cancellationToken),
            outputKind = request.outputKind,
            scratchDirectory = request.workingDirectory,
            compilerFamily = id,
            compilerPath = m_toolchain.compiler,
            dependencyScans = scans.ToImmutable(),
            dependencyFiles = dependencies.ToImmutable(),
            preprocessingOperations = NativePreprocessingPlanner.Create(request, id, scans),
            includeSearchDirectories = request.sources.Select(unit => Path.GetDirectoryName(unit.path)!).Concat(request.includeDirectories).Concat(request.systemIncludeDirectories)
                .Concat(m_toolchain.includeDirectories).Distinct(StringComparer.OrdinalIgnoreCase).ToImmutableArray(),
            externalInputFiles = commands.Select(command => command.executable).Concat(request.libraries.Where(Path.IsPathFullyQualified)).Distinct(StringComparer.OrdinalIgnoreCase).ToImmutableArray(),
            librarySearchDirectories = m_toolchain.libraryDirectories.Concat(request.libraryDirectories).Distinct(StringComparer.OrdinalIgnoreCase).ToImmutableArray()
            ,
            linkDependencyScans = linkScans.ToImmutable()
        }, request);
    }

    private string Standard(NativeCompilationUnit unit) => (unit.language, unit.standard) switch
    {
        (SourceLanguage.C, "c11") => "/std:c11",
        (SourceLanguage.C, "c17") => "/std:c17",
        (SourceLanguage.Cpp, "c++14") => "/std:c++14",
        (SourceLanguage.Cpp, "c++17") => "/std:c++17",
        (SourceLanguage.Cpp, "c++20") => "/std:c++20",
        (SourceLanguage.Cpp, "c++23") when m_toolchain.compilerVersion >= new Version(19, 52) => "/std:c++23",
        (SourceLanguage.Cpp, "c++23") when m_toolchain.compilerVersion >= new Version(19, 43) => "/std:c++23preview",
        _ => throw new NotSupportedException("The selected MSVC toolchain has no supported exact mode for " + unit.standard + ".")
    };
}
