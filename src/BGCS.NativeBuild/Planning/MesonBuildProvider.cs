using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;

namespace BGCS.NativeBuild.Planning;

/// <summary>Runs exact frozen compiler operations through Meson custom targets and explicit dependencies.</summary>
public sealed class MesonBuildProvider : INativeBuildProvider, INativeBuildPrerequisiteProvider
{
    private readonly INativeBuildProvider m_compiler;
    private readonly string m_meson;
    private readonly string m_ninja;
    /// <summary>Composes an exact native compiler with an installed Meson executable.</summary>
    /// <param name="compiler">The underlying exact target compiler provider.</param>
    /// <param name="meson">The absolute Meson executable.</param>
    /// <param name="ninja">The absolute Ninja executable selected for all Meson operations.</param>
    public MesonBuildProvider(
        INativeBuildProvider compiler,
        string meson,
        string ninja
    ) {
        m_compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        if (!Path.IsPathFullyQualified(meson) || !File.Exists(meson)) throw new FileNotFoundException("Meson must exist at an absolute path.", meson);
        m_meson = meson;
        if (!Path.IsPathFullyQualified(ninja) || !File.Exists(ninja)) throw new FileNotFoundException("Ninja must exist at an absolute path.", ninja);
        m_ninja = ninja;
    }
    /// <inheritdoc/>
    public string id => "meson";
    /// <inheritdoc/>
    public ImmutableArray<NativeToolPrerequisite> DescribePrerequisites() => (m_compiler is INativeBuildPrerequisiteProvider prerequisites ? prerequisites.DescribePrerequisites()
        : ImmutableArray.Create(new NativeToolPrerequisite("compiler", m_compiler.id, null)))
        .AddRange(ImmutableArray.Create(new NativeToolPrerequisite("buildSystem", "meson", m_meson, minimumVersion: "1.0.0"), new NativeToolPrerequisite("backend", "ninja", m_ninja)));
    /// <inheritdoc/>
    public NativeBuildPlan Plan(
        NativeBuildRequest request,
        CancellationToken cancellationToken
    ) {
        string project = Path.Combine(request.workingDirectory, "meson-project"), build = Path.Combine(request.workingDirectory, "meson-build");
        string argumentDirectory = NativeBuildEnvironment.ArgumentDirectory(request);
        NativeBuildPlan direct = m_compiler.Plan(request with { workingDirectory = build, argumentWorkingDirectory = argumentDirectory, outputFile = Path.Combine(build, Path.GetFileName(request.outputFile)) }, cancellationToken);
        if (!direct.generatedInputFiles.IsEmpty) throw new NotSupportedException("A Meson compiler adapter cannot wrap another generated build system.");
        StringBuilder text = new("project('bgcs_native', version: '3.0.0', meson_version: '>=1.0.0')\nbgcs_python = import('python').find_installation()\nbgcs_runner = files('bgcs-run-command.py')\n");
        ImmutableArray<NativeBuildInput>.Builder generated = ImmutableArray.CreateBuilder<NativeBuildInput>();
        generated.Add(new("meson-project/bgcs-run-command.py", ProcessRunner));
        for (int index = 0; index < direct.commands.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NativeBuildCommand command = direct.commands[index];
            if (command.outputFiles.Length != 1) throw new NotSupportedException("Meson compiler operations must declare one primary output.");
            string name = "operation_" + index;
            string operationPath = "meson-project/operation-" + index + ".json";
            generated.Add(new(operationPath, JsonSerializer.Serialize(new { command.executable, command.arguments, command.workingDirectory, command.environmentOverrides })));
            text.Append(name).Append(" = custom_target(").Append(Quote(command.id)).Append(",\n  output: ").Append(Quote(Path.GetFileName(command.outputFiles[0])));
            text.Append(",\n  command: [bgcs_python, bgcs_runner, ").Append(Quote(Path.Combine(request.workingDirectory, operationPath.Replace('/', Path.DirectorySeparatorChar))));
            text.Append("],\n  depend_files: [").AppendJoin(", ", command.inputFiles.Where(File.Exists).Append(command.executable).Append(Path.Combine(request.workingDirectory, operationPath.Replace('/', Path.DirectorySeparatorChar))).Select(Quote)).Append("]");
            if (index > 0) text.Append(",\n  depends: [").AppendJoin(", ", Enumerable.Range(0, index).Select(value => "operation_" + value)).Append("]");
            text.Append(",\n  build_by_default: true");
            if (index == direct.commands.Length - 1) text.Append(",\n  install: true,\n  install_dir: ").Append(Quote(Path.GetDirectoryName(request.outputFile)!));
            text.Append(")\n");
        }
        text.Append("alias_target('bgcs_native_objects', ").AppendJoin(", ", direct.commands.Select((
            command,
            index
        ) => (command, index)).Where(item => item.command.id.StartsWith("compile-", StringComparison.Ordinal)).Select(item => "operation_" + item.index)).Append(")\n");
        ImmutableArray<NativeDependencyScan> scans = direct.dependencyScans.Select((
            scan,
            index
        ) => {
            string path = Path.Combine(request.workingDirectory, "unit-" + index + ".scan" + Path.GetExtension(scan.dependencyFile.path));
            var outputs = scan.command.outputFiles.ToDictionary(output => output,
                output => output == scan.dependencyFile.path ? path : Path.Combine(request.workingDirectory, Path.GetFileName(output)), StringComparer.Ordinal);
            return scan with
            {
                command = scan.command with { arguments = scan.command.arguments.Select(Relocate).ToImmutableArray(), outputFiles = scan.command.outputFiles.Select(output => outputs[output]).ToImmutableArray() },
                dependencyFile = scan.dependencyFile with { path = path }
            };

            string Relocate(string argument)
            {
                if (outputs.TryGetValue(argument, out string? mapped)) return mapped;
                if (argument.StartsWith("/clang:", StringComparison.Ordinal) && outputs.TryGetValue(argument[7..], out mapped)) return "/clang:" + mapped;
                if (argument.StartsWith("/Fi", StringComparison.OrdinalIgnoreCase) && outputs.TryGetValue(argument[3..], out mapped)) return "/Fi" + mapped;
                return argument;
            }
        }).ToImmutableArray();
        generated.Add(new("meson-project/meson.build", text.ToString()));
        // Meson's Python commands print native paths to redirected streams.
        // Freeze UTF-8 in the child plan instead of inheriting a Windows ANSI
        // code page that cannot represent otherwise valid source/output paths.
        ImmutableDictionary<string, string?> environment = NativeBuildEnvironment.cleanCompiler.SetItem("NINJA", m_ninja)
            .SetItem("PYTHONUTF8", "1").SetItem("PYTHONIOENCODING", "utf-8");
        return direct with
        {
            providerId = id,
            outputFile = request.outputFile,
            scratchDirectory = request.workingDirectory,
            externalInputFiles = direct.externalInputFiles.AddRange(ImmutableArray.Create(m_meson, m_ninja)).Distinct(StringComparer.Ordinal).ToImmutableArray(),
            generatedInputFiles = generated.ToImmutable(),
            dependencyScans = scans,
            linkDependencyScans = direct.linkDependencyScans.Select(scan => scan with { finalCommandId = "build" }).ToImmutableArray(),
            commands = [new("configure", m_meson, ["setup", build, project, "--backend=ninja", "--buildtype=release"], request.workingDirectory) { environmentOverrides = environment, outputEncoding = "utf-8" },
                new("objects", m_meson, ["compile", "-C", build, "-j", "2", "bgcs_native_objects"], request.workingDirectory) { environmentOverrides = environment, outputEncoding = "utf-8" },
                new("build", m_meson, ["compile", "-C", build, "-j", "2"], request.workingDirectory) { environmentOverrides = environment, outputEncoding = "utf-8", linkDependencyTrace = direct.commands[^1].linkDependencyTrace },
                new("install", m_meson, ["install", "-C", build, "--no-rebuild"], request.workingDirectory) { outputFiles = [request.outputFile], environmentOverrides = environment, outputEncoding = "utf-8" }]
        };
    }
    private const string ProcessRunner = """
        import json
        import os
        import subprocess
        import sys

        with open(sys.argv[1], encoding='utf-8') as stream:
            operation = json.load(stream)
        environment = os.environ.copy()
        for name, value in operation['environmentOverrides'].items():
            if value is None:
                environment.pop(name, None)
            else:
                environment[name] = value
        result = subprocess.run([operation['executable'], *operation['arguments']],
                                cwd=operation['workingDirectory'], env=environment, shell=False)
        raise SystemExit(result.returncode)
        """;
    private static string Quote(string value)
    {
        if (value.Contains('\0') || value.Contains('\n') || value.Contains('\r') || value.Contains("@INPUT", StringComparison.Ordinal) || value.Contains("@OUTPUT", StringComparison.Ordinal)
            || value.Contains("@DEPFILE@", StringComparison.Ordinal) || value.Contains("@PLAINNAME@", StringComparison.Ordinal) || value.Contains("@BASENAME@", StringComparison.Ordinal))
            throw new ArgumentException("A Meson build argument cannot contain command substitutions or control characters.");
        return "'" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal) + "'";
    }
}
