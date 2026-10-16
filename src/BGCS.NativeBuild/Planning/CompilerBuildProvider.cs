using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;

namespace BGCS.NativeBuild.Planning;

/// <summary>Plans explicit Clang and GNU compilation units followed by a shared-library link.</summary>
public sealed class CompilerBuildProvider : INativeBuildProvider, INativeBuildPrerequisiteProvider
{
    private readonly string m_compiler;
    private readonly string? m_archiver;
    private readonly string? m_linker;
    /// <summary>Captures an explicitly resolved tool and driver family.</summary>
    /// <param name="id">Clang or GNU driver identity.</param>
    /// <param name="compiler">The resolved native compiler executable.</param>
    /// <param name="archiver">An optional matching archive tool required by static library construction.</param>
    /// <param name="linker">The exact pinned linker, or null when its implicit driver selection has not been independently resolved.</param>
    public CompilerBuildProvider(
        string id,
        string compiler,
        string? archiver = null,
        string? linker = null
    ) {
        if (id is not ("clang" or "gnu")) throw new ArgumentException("The compiler driver family must be clang or gnu.", nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(compiler);
        if (id == "gnu" && linker is not null) throw new NotSupportedException("An arbitrary GNU linkerPath has no independently enforced driver mapping; use a provider with explicit linker execution.");
        this.id = id;
        m_compiler = compiler;
        m_archiver = archiver;
        m_linker = linker;
    }
    /// <inheritdoc/>
    public string id { get; }
    /// <inheritdoc/>
    public ImmutableArray<NativeToolPrerequisite> DescribePrerequisites()
    {
        ImmutableArray<NativeToolPrerequisite> tools = [new("compiler", id, m_compiler), new("archive", id == "clang" ? "llvm-ar" : "ar", m_archiver, NativeOutputKind.StaticLibrary)];
        return m_linker is null ? tools : tools.Add(new("linker", "native-linker", m_linker, NativeOutputKind.SharedLibrary));
    }
    /// <inheritdoc/>
    public NativeBuildPlan Plan(
        NativeBuildRequest request,
        CancellationToken cancellationToken
    )
        => PlanCore(request, cancellationToken, preprocessingOnly: false);

    // Parsing self-contained headers does not require the libraries/linker/sysroot
    // demanded by a later native artifact build. The actual compiler still resolves
    // every requested include, and missing SDK headers remain preprocessing errors.
    internal NativeBuildPlan PlanPreprocessing(
        NativeBuildRequest request,
        CancellationToken cancellationToken
    )
        => PlanCore(request, cancellationToken, preprocessingOnly: true);

    private NativeBuildPlan PlanCore(
        NativeBuildRequest request,
        CancellationToken cancellationToken,
        bool preprocessingOnly
    ) {
        ArgumentNullException.ThrowIfNull(request);
        string argumentDirectory = NativeBuildEnvironment.ArgumentDirectory(request);
        string? pinnedLinker = preprocessingOnly ? null : request.target.toolchain.linkerPath ?? m_linker;
        if (id == "gnu" && pinnedLinker is not null) throw new NotSupportedException("An arbitrary GNU linkerPath has no independently enforced driver mapping; use a provider with explicit linker execution.");
        if (pinnedLinker is not null && (!Path.IsPathFullyQualified(pinnedLinker) || !File.Exists(pinnedLinker))) throw new FileNotFoundException("The pinned native linker must exist at its exact absolute path.", pinnedLinker);
        if (m_linker is not null && request.target.toolchain.linkerPath is string declaredLinker && !Path.GetFullPath(m_linker).Equals(Path.GetFullPath(declaredLinker), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("The native provider and target descriptor disagree on their pinned linker.");
        if (pinnedLinker is not null) NativeResponseFileInputs.RejectPinnedLinkerOverrides(request, id, m_compiler, cancellationToken);
        if (!Enum.IsDefined(request.outputKind)) throw new ArgumentOutOfRangeException(nameof(request));
        if (request.sources.IsDefaultOrEmpty) throw new ArgumentException("At least one native compilation unit is required.", nameof(request));
        if (!preprocessingOnly && request.target.platformId is not ("windows" or "linux" or "macos" or "android" or "freebsd" or "ios" or "ios-simulator" or "emscripten")) throw new NotSupportedException("The native compiler provider requires a recognized target family.");
        if (id == "gnu" && request.target.platformId is not ("linux" or "freebsd" or "macos"))
            throw new NotSupportedException("GNU native construction currently supports Linux, FreeBSD and macOS target families. Android and Apple mobile targets require their SDK Clang provider.");
        if (request.target.platformId == "linux" && request.target.abiId is not ("gnu" or "musl")) throw new NotSupportedException("The Linux target must declare GNU or musl ABI.");
        if (request.target.platformId is "ios" or "ios-simulator" or "emscripten" && request.outputKind == NativeOutputKind.SharedLibrary)
            throw new NotSupportedException("This target requires static native linking; select archive or object output.");
        if (!preprocessingOnly && (request.target.platformId is "android" or "emscripten" || request.target.abiId == "musl"))
            if (request.target.toolchain.sysRoot is null) throw new InvalidOperationException("This target requires an explicit verified SDK sysroot before native construction.");
        List<NativeBuildCommand> commands = [];
        List<NativeDependencyScan> scans = [];
        List<NativeDependencyFile> dependencies = [];
        List<NativeLinkDependencyScan> linkScans = [];
        List<string> objects = [];
        List<string> common = [];
        if (id == "clang") common.AddRange(["--no-default-config", "--target=" + request.target.triple]);
        if (id == "clang") common.AddRange(request.target.toolchain.arguments.Where(argument => argument.StartsWith("-fms-compatibility-version=", StringComparison.Ordinal)));
        if (request.target.platformId == "windows" && request.target.toolchain.sdkVersion is not null)
            common.AddRange(["-nostdlibinc", "-nostdinc++"]);
        if (request.target.platformId != "windows" && request.target.toolchain.sysRoot is string sysroot) { common.Add("--sysroot"); common.Add(sysroot); }
        if (request.target.platformId is not ("windows" or "emscripten")) common.Add("-fPIC");
        foreach (string include in request.includeDirectories) { common.Add("-I"); common.Add(include); }
        ImmutableArray<string> systemIncludes = request.systemIncludeDirectories.Concat(request.target.toolchain.systemIncludeFolders)
            .Concat(request.target.toolchain.cxxSystemIncludeFolders).Distinct(StringComparer.Ordinal).ToImmutableArray();
        for (int index = 0; index < request.sources.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NativeCompilationUnit unit = request.sources[index];
            if (unit.language is not (SourceLanguage.C or SourceLanguage.Cpp)) throw new NotSupportedException("Each native source must declare C or C++ language.");
            if (!Path.IsPathFullyQualified(unit.path) || !File.Exists(unit.path)) throw new FileNotFoundException("A native source input is missing or not absolute.", unit.path);
            string objectFile = request.target.platformId == "windows" && request.outputKind == NativeOutputKind.Object && request.sources.Length == 1
                ? request.outputFile : Path.Combine(request.workingDirectory, "unit-" + index.ToString(CultureInfo.InvariantCulture) + ".o");
            ImmutableArray<string> unitIncludes = unit.language == SourceLanguage.Cpp
                ? request.target.toolchain.cxxSystemIncludeFolders.Concat(systemIncludes).Distinct(StringComparer.Ordinal).ToImmutableArray()
                : systemIncludes.Where(include => !request.target.toolchain.cxxSystemIncludeFolders.Contains(include, StringComparer.Ordinal)).ToImmutableArray();
            List<string> unitCommon = new(common);
            foreach (string include in unitIncludes) { unitCommon.Add("-isystem"); unitCommon.Add(include); }
            unitCommon.AddRange(request.defines.Select(define => "-D" + define));
            unitCommon.AddRange(request.compilerArguments);
            List<string> arguments = new(unitCommon);
            string suffix = index.ToString(CultureInfo.InvariantCulture), dependency = Path.Combine(request.workingDirectory, "unit-" + suffix + ".d"), scanDependency = Path.Combine(request.workingDirectory, "unit-" + suffix + ".scan.d");
            List<string> scanArguments = new(unitCommon);
            scanArguments.AddRange(["-x", unit.language == SourceLanguage.C ? "c" : "c++", "-std=" + unit.standard, "-M", "-MF", scanDependency, unit.path]);
            scans.Add(new(new("dependencies-" + suffix, m_compiler, scanArguments.ToImmutableArray(), argumentDirectory) { inputFiles = [unit.path], outputFiles = [scanDependency], environmentOverrides = NativeBuildEnvironment.cleanCompiler },
                new(scanDependency, NativeDependencyFormat.Make, unit.path) { workingDirectory = argumentDirectory }));
            dependencies.Add(new(dependency, NativeDependencyFormat.Make, unit.path) { workingDirectory = argumentDirectory });
            arguments.AddRange(["-x", unit.language == SourceLanguage.C ? "c" : "c++", "-std=" + unit.standard, "-MD", "-MF", dependency, "-c", unit.path, "-o", objectFile]);
            commands.Add(new("compile-" + index.ToString(CultureInfo.InvariantCulture), m_compiler, arguments.ToImmutableArray(), argumentDirectory) { inputFiles = [unit.path], outputFiles = [objectFile], environmentOverrides = NativeBuildEnvironment.cleanCompiler });
            objects.Add(objectFile);
        }
        List<string> link = [];
        if (request.target.platformId == "windows" && request.outputKind == NativeOutputKind.Object)
        {
            if (request.sources.Length != 1) throw new NotSupportedException("COFF object output requires one compilation unit; use an archive for multiple sources.");
            return Finish();
        }
        if (request.outputKind == NativeOutputKind.StaticLibrary)
        {
            if (m_archiver is null || !File.Exists(m_archiver)) throw new FileNotFoundException("Static construction requires a resolved matching native archive tool.", m_archiver);
            if (!request.libraries.IsEmpty || !request.libraryDirectories.IsEmpty || !request.linkerArguments.IsEmpty)
                throw new NotSupportedException("Static libraries preserve external dependencies for the consumer; they cannot absorb linker libraries or flags.");
            commands.Add(new("archive", m_archiver, new[] { "rcs", request.outputFile }.Concat(objects).ToImmutableArray(), argumentDirectory) { inputFiles = objects.ToImmutableArray(), outputFiles = [request.outputFile], environmentOverrides = NativeBuildEnvironment.cleanCompiler });
            return Finish();
        }
        if (request.target.platformId == "windows")
        {
            string linker = pinnedLinker ?? throw new InvalidOperationException("A Windows Clang build requires a resolved, pinned MSVC SDK and linker.");
            string runtime = request.compilerArguments.LastOrDefault(argument => argument.StartsWith("-fms-runtime-lib=", StringComparison.Ordinal))?["-fms-runtime-lib=".Length..] ?? "static";
            string crt = runtime switch { "static" => "libcmt", "static_dbg" => "libcmtd", "dll" => "msvcrt", "dll_dbg" => "msvcrtd", _ => throw new NotSupportedException("The requested Windows Clang runtime library form is unknown.") };
            link.AddRange(["/NOLOGO", "/DLL", "/VERBOSE:LIB", "/MACHINE:" + request.target.architectureId.ToUpperInvariant(), "/DEFAULTLIB:" + crt, "/DEFAULTLIB:oldnames"]);
            link.AddRange(objects);
            link.AddRange(request.target.toolchain.libraryDirectories.Concat(request.libraryDirectories).Distinct(StringComparer.OrdinalIgnoreCase).Select(directory => "/LIBPATH:" + directory));
            link.AddRange(request.libraries.Select(library => Path.IsPathFullyQualified(library) ? library : Path.HasExtension(library) ? throw new ArgumentException("Native library file paths must be absolute before planning.") : library + ".lib"));
            for (int argument = 0; argument < request.linkerArguments.Length; argument++)
            {
                string flag = request.linkerArguments[argument];
                if (flag.StartsWith("-Wl,", StringComparison.Ordinal)) link.AddRange(flag[4..].Split(','));
                else if (flag == "-Xlinker")
                {
                    if (++argument >= request.linkerArguments.Length) throw new ArgumentException("A -Xlinker flag requires one native linker argument.");
                    link.Add(request.linkerArguments[argument]);
                }
                else link.Add(flag);
            }
            link.Add("/OUT:" + request.outputFile);
            link.Add("/IMPLIB:" + Path.Combine(request.workingDirectory, "native-final.lib"));
            link.Add("/PDB:" + Path.Combine(request.workingDirectory, "native-final.pdb"));
            commands.Add(new("link", linker, link.ToImmutableArray(), argumentDirectory) { inputFiles = objects.ToImmutableArray(), outputFiles = [request.outputFile], environmentOverrides = NativeBuildEnvironment.cleanCompiler, linkDependencyTrace = NativeLinkTraceFormat.MsvcLibrarySearch });
            string candidate = Path.Combine(request.workingDirectory, "native-link-probe.dll");
            List<string> windowsProbe = new(link.Take(link.Count - 3)) { "/OUT:" + candidate, "/IMPLIB:" + Path.Combine(request.workingDirectory, "native-probe.lib"), "/PDB:" + Path.Combine(request.workingDirectory, "native-probe.pdb") };
            linkScans.Add(new("link", new("dependencies-link", linker, windowsProbe.ToImmutableArray(), argumentDirectory) { inputFiles = objects.ToImmutableArray(), outputFiles = [candidate], environmentOverrides = NativeBuildEnvironment.cleanCompiler }, NativeLinkTraceFormat.MsvcLibrarySearch));
            return Finish();
        }
        if (id == "clang") link.AddRange(["--no-default-config", "--target=" + request.target.triple]);
        if (id == "clang" && request.sources.Any(unit => unit.language == SourceLanguage.Cpp)) link.Add("--driver-mode=g++");
        if (request.target.platformId != "windows" && request.target.toolchain.sysRoot is string linkSysroot) { link.Add("--sysroot"); link.Add(linkSysroot); }
        if (request.outputKind == NativeOutputKind.Object) link.Add("-nostdlib");
        link.Add(request.outputKind == NativeOutputKind.Object ? "-r" : request.target.platformId == "macos" ? "-dynamiclib" : "-shared");
        link.AddRange(objects);
        foreach (string directory in request.target.toolchain.libraryDirectories.Concat(request.libraryDirectories).Distinct(StringComparer.Ordinal)) { link.Add("-L"); link.Add(directory); }
        foreach (string library in request.libraries)
            link.Add(Path.IsPathRooted(library) ? library : library.Contains('/') || library.Contains('\\') || Path.HasExtension(library)
                ? throw new ArgumentException("Native library file paths must be absolute before planning.", nameof(request)) : "-l" + library);
        link.AddRange(request.linkerArguments);
        if (pinnedLinker is not null) link.Add("--ld-path=" + pinnedLinker);
        NativeLinkTraceFormat traceFormat = request.target.platformId == "windows" ? NativeLinkTraceFormat.MsvcLibrarySearch : NativeLinkTraceFormat.GnuLike;
        // lld-link accepts /VERBOSE:LIB for compatibility but does not emit its input trace.
        // /VERBOSE reports resolved archive paths in both lld-link and the Microsoft linker.
        link.Add(request.target.platformId == "windows" ? "-Wl,/VERBOSE" : "-Wl,-t");
        link.AddRange(["-o", request.outputFile]);
        commands.Add(new("link", m_compiler, link.ToImmutableArray(), argumentDirectory) { inputFiles = objects.ToImmutableArray(), outputFiles = [request.outputFile], environmentOverrides = NativeBuildEnvironment.cleanCompiler, linkDependencyTrace = traceFormat });
        string probe = Path.Combine(request.workingDirectory, "native-link-probe" + Path.GetExtension(request.outputFile));
        List<string> probeLink = new(link);
        probeLink[^1] = probe;
        linkScans.Add(new("link", new("dependencies-link", m_compiler, probeLink.ToImmutableArray(), argumentDirectory) { inputFiles = objects.ToImmutableArray(), outputFiles = [probe], environmentOverrides = NativeBuildEnvironment.cleanCompiler }, traceFormat));
        return Finish();

        NativeBuildPlan Finish() => NativePreprocessingPlanner.AppendSources(new(id, request.target, commands.ToImmutableArray(), request.outputFile)
        {
            argumentInputs = NativeResponseFileInputs.CaptureRequest(request, id, cancellationToken, m_compiler),
            outputKind = request.outputKind,
            scratchDirectory = request.workingDirectory,
            compilerFamily = id,
            compilerPath = m_compiler,
            dependencyScans = scans.ToImmutableArray(),
            dependencyFiles = dependencies.ToImmutableArray(),
            preprocessingOperations = NativePreprocessingPlanner.Create(request, id, scans),
            includeSearchDirectories = request.sources.Select(unit => Path.GetDirectoryName(unit.path)!).Concat(request.includeDirectories).Concat(systemIncludes).Distinct(StringComparer.Ordinal).ToImmutableArray(),
            externalInputFiles = commands.Select(command => command.executable).Concat(pinnedLinker is string linker ? [linker] : [])
                .Concat(request.libraries.Where(Path.IsPathFullyQualified)).Distinct(StringComparer.Ordinal).ToImmutableArray(),
            // No linker operation executes for the private source-view plan.
            // Keep explicit -L and other driver inputs in argumentInputs, but
            // do not treat implicit link-only SDK directories as header inputs.
            librarySearchDirectories = preprocessingOnly ? []
                : request.target.toolchain.libraryDirectories.Concat(request.libraryDirectories).Distinct(StringComparer.Ordinal).ToImmutableArray()
            ,
            linkDependencyScans = linkScans.ToImmutableArray()
        }, request);
    }
}
