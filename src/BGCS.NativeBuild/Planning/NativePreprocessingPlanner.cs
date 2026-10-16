using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Requests;

namespace BGCS.NativeBuild.Planning;

internal static class NativePreprocessingPlanner
{
    internal static ImmutableArray<NativePreprocessingOperation> Create(
        NativeBuildRequest request,
        string family,
        IEnumerable<NativeDependencyScan> scans
    ) {
        ImmutableArray<NativePreprocessingOperation>.Builder result = ImmutableArray.CreateBuilder<NativePreprocessingOperation>();
        int index = 0;
        foreach (NativeDependencyScan scan in scans)
        {
            NativeCompilationUnit unit = request.sources.Single(source => source.path == scan.dependencyFile.source);
            result.Add(CreateOperation(request, family, scan, unit, "unit-" + (index++).ToString(CultureInfo.InvariantCulture)));
        }
        return result.ToImmutable();
    }

    internal static NativeBuildPlan AppendSources(
        NativeBuildPlan plan,
        NativeBuildRequest request
    ) {
        if (request.preprocessingSources.IsDefault) throw new ArgumentException("Preprocessing-only native sources must be initialized.", nameof(request));
        if (request.preprocessingSources.IsEmpty) return plan;
        string family = plan.compilerFamily ?? plan.providerId;
        StringComparer paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        HashSet<string> seen = request.sources.Select(source => Path.GetFullPath(source.path)).ToHashSet(paths);
        int index = 0;
        foreach (NativeCompilationUnit unit in request.preprocessingSources)
        {
            if (!Path.IsPathFullyQualified(unit.path) || !File.Exists(unit.path)) throw new FileNotFoundException("A preprocessing-only input must exist at its exact absolute path.", unit.path);
            if (!seen.Add(Path.GetFullPath(unit.path))) throw new ArgumentException("A preprocessing-only input cannot duplicate a compiled or other preprocessing source.", nameof(request));
            NativeCompilationUnit anchor = request.sources.FirstOrDefault(source => source.language == unit.language && source.standard == unit.standard)
                ?? throw new NotSupportedException("A preprocessing-only input requires a compiled source with exactly matching language and standard.");
            NativeDependencyScan template = plan.dependencyScans.Single(candidate => paths.Equals(candidate.dependencyFile.source, anchor.path));
            string name = "facts-" + (index++).ToString(CultureInfo.InvariantCulture);
            string dependency = Path.Combine(request.workingDirectory, name + (template.dependencyFile.format == NativeDependencyFormat.MsvcJson ? ".scan.json" : ".scan.d"));
            ImmutableArray<string> arguments = template.command.arguments.Select(argument => paths.Equals(argument, anchor.path) ? unit.path
                : paths.Equals(argument, template.dependencyFile.path) ? dependency
                : argument.StartsWith("/clang:", StringComparison.Ordinal) && paths.Equals(argument[7..], template.dependencyFile.path) ? "/clang:" + dependency : argument).ToImmutableArray();
            NativeDependencyScan scan = new(template.command with
            {
                id = "dependencies-" + name,
                arguments = arguments,
                inputFiles = [unit.path],
                outputFiles = [dependency]
            }, template.dependencyFile with { path = dependency, source = unit.path });
            NativePreprocessingOperation operation = CreateOperation(request, family, scan, unit, name) with { preprocessingOnly = true };
            if (family == "msvc")
            {
                // Facts inputs may contain parser-only scalar deduction declarations.
                // MSVC /Zs performs semantic analysis, so obtain their independent
                // dependency closure with a separate preprocessing-only operation.
                string preflight = Path.Combine(request.workingDirectory, name + ".preflight." + (unit.language == SourceLanguage.C ? "c" : "cpp"));
                scan = scan with
                {
                    command = scan.command with
                    {
                        arguments = scan.command.arguments.Where(argument => argument != "/Zs").ToImmutableArray().Add("/P").Add("/Fi" + preflight),
                        outputFiles = [dependency, preflight]
                    }
                };
            }
            plan = plan with
            {
                dependencyScans = plan.dependencyScans.Add(scan),
                preprocessingOperations = plan.preprocessingOperations.Add(operation),
                externalInputFiles = plan.externalInputFiles.Add(unit.path),
                includeSearchDirectories = plan.includeSearchDirectories.Add(Path.GetDirectoryName(unit.path)!).Distinct(paths).ToImmutableArray()
            };
        }
        return plan;
    }

    private static NativePreprocessingOperation CreateOperation(
        NativeBuildRequest request,
        string family,
        NativeDependencyScan scan,
        NativeCompilationUnit unit,
        string name
    ) {
        string output = Path.Combine(request.workingDirectory, name + ".preprocessed." + (unit.language == SourceLanguage.C ? "c" : "cpp"));
        string dependencies = Path.Combine(request.workingDirectory, name + (scan.dependencyFile.format == NativeDependencyFormat.MsvcJson ? ".preprocessed.json" : ".preprocessed.d"));
        List<string> arguments = [];
        for (int position = 0; position < scan.command.arguments.Length; position++)
        {
            string argument = scan.command.arguments[position];
            if (family == "msvc" && argument == "/sourceDependencies") { position++; continue; }
            if (family == "msvc" && argument is "/Zs" or "/c") continue;
            if (family == "clang-cl" && argument == "/clang:-MF") { position++; continue; }
            if (family == "clang-cl" && argument is "/clang:-M" or "/clang:-MD" or "/c") continue;
            if (family is "clang" or "gnu" && argument == "-MF") { position++; continue; }
            if (family is "clang" or "gnu" && argument is "-M" or "-MD" or "-MMD" or "-MM") continue;
            arguments.Add(argument);
        }
        if (family == "msvc") arguments.AddRange(["/P", "/Fi" + output, "/sourceDependencies", dependencies]);
        else if (family == "clang-cl") arguments.AddRange(["/P", "/Fi" + output, "/clang:-MD", "/clang:-MF", "/clang:" + dependencies]);
        else if (family is "clang" or "gnu")
        {
            arguments.AddRange(["-E", "-o", output, "-MD", "-MF", dependencies]);
            if (family == "gnu" && unit.language == SourceLanguage.Cpp) arguments.Add("-dD");
        }
        else throw new NotSupportedException("The native compiler family has no exact preprocessing command: " + family);
        NativeBuildCommand command = scan.command with
        {
            id = "preprocess-" + name,
            arguments = arguments.ToImmutableArray(),
            outputFiles = [output, dependencies]
        };
        return new(command, unit.path, unit.language, unit.standard, output)
        { dependencyFile = scan.dependencyFile with { path = dependencies } };
    }
}
