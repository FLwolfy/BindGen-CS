using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;

namespace BGCS.NativeBuild.Planning;

/// <summary>Runs exact frozen compiler operations through a generated CMake dependency graph.</summary>
public sealed class CMakeBuildProvider : INativeBuildProvider, INativeBuildPrerequisiteProvider
{
    private readonly INativeBuildProvider m_compiler;
    private readonly string m_cmake;
    private readonly string m_ninja;
    /// <summary>Composes a resolved native compiler with installed CMake and Ninja executables.</summary>
    /// <param name="compiler">The underlying exact target compiler provider.</param>
    /// <param name="cmake">The absolute CMake executable.</param>
    /// <param name="ninja">The absolute Ninja executable.</param>
    public CMakeBuildProvider(
        INativeBuildProvider compiler,
        string cmake,
        string ninja
    ) {
        m_compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        m_cmake = RequireTool(cmake);
        m_ninja = RequireTool(ninja);
    }
    /// <inheritdoc/>
    public string id => "cmake";
    /// <inheritdoc/>
    public ImmutableArray<NativeToolPrerequisite> DescribePrerequisites() => (m_compiler is INativeBuildPrerequisiteProvider prerequisites ? prerequisites.DescribePrerequisites()
        : ImmutableArray.Create(new NativeToolPrerequisite("compiler", m_compiler.id, null)))
        .AddRange(ImmutableArray.Create(new NativeToolPrerequisite("buildSystem", "cmake", m_cmake, minimumVersion: "3.24.0"), new NativeToolPrerequisite("backend", "ninja", m_ninja)));
    /// <inheritdoc/>
    public NativeBuildPlan Plan(
        NativeBuildRequest request,
        CancellationToken cancellationToken
    ) {
        NativeBuildPlan direct = m_compiler.Plan(request, cancellationToken);
        if (!direct.generatedInputFiles.IsEmpty) throw new NotSupportedException("A CMake compiler adapter cannot wrap another generated build system.");
        string project = Path.Combine(request.workingDirectory, "cmake-project"), build = Path.Combine(request.workingDirectory, "cmake-build");
        StringBuilder text = new("cmake_minimum_required(VERSION 3.24)\nproject(bgcs_native LANGUAGES NONE)\n");
        ImmutableArray<NativeBuildInput>.Builder generated = ImmutableArray.CreateBuilder<NativeBuildInput>();
        int operation = 0;
        foreach (NativeBuildCommand command in direct.commands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (command.outputFiles.IsDefaultOrEmpty) throw new InvalidOperationException("CMake compiler operations must declare outputs.");
            string relative = "cmake-project/operation-" + (operation++).ToString(System.Globalization.CultureInfo.InvariantCulture) + ".cmake";
            string script = Path.Combine(request.workingDirectory, relative);
            StringBuilder run = new("cmake_minimum_required(VERSION 3.24)\nexecute_process(\n  COMMAND ");
            bool hasEnvironment = !command.environmentOverrides.IsEmpty;
            if (hasEnvironment)
            {
                run.Append(Bracket(m_cmake)).Append(" -E env");
                foreach ((string key, string? value) in command.environmentOverrides.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                    run.Append(' ').Append(Bracket(value is null ? "--unset=" + key : key + "=" + value));
                run.Append(" -- ");
            }
            run.Append(Bracket(command.executable));
            foreach (string argument in command.arguments) run.Append(' ').Append(Bracket(argument));
            run.Append("\n  WORKING_DIRECTORY ").Append(Bracket(command.workingDirectory)).Append("\n  RESULT_VARIABLE bgcs_exit\n)\nif(NOT bgcs_exit STREQUAL \"0\")\n  message(FATAL_ERROR \"Frozen BGCS native operation failed: ${bgcs_exit}\")\nendif()\n");
            generated.Add(new(relative, run.ToString()));
            text.Append("add_custom_command(OUTPUT ").AppendJoin(' ', command.outputFiles.Select(Quote)).Append("\n  COMMAND ").Append(Quote(m_cmake)).Append(" -P ").Append(Quote(script));
            text.Append("\n  DEPENDS ").AppendJoin(' ', command.inputFiles.Append(command.executable).Append(script).Select(Quote));
            text.Append("\n  VERBATIM)\n");
        }
        text.Append("add_custom_target(bgcs_native_objects DEPENDS ").AppendJoin(' ', direct.commands.Where(command => command.id.StartsWith("compile-", StringComparison.Ordinal)).SelectMany(command => command.outputFiles).Select(Quote)).Append(")\n");
        text.Append("add_custom_target(bgcs_native_artifact ALL DEPENDS ").Append(Quote(direct.outputFile)).Append(")\n");
        ImmutableArray<NativeBuildCommand> commands =
        [
            new("configure", m_cmake, ["-S", project, "-B", build, "-G", "Ninja", "-DCMAKE_MAKE_PROGRAM=" + m_ninja], request.workingDirectory) { environmentOverrides = NativeBuildEnvironment.cleanCompiler },
            new("objects", m_cmake, ["--build", build, "--target", "bgcs_native_objects", "--parallel", "2"], request.workingDirectory) { environmentOverrides = NativeBuildEnvironment.cleanCompiler },
            new("build", m_cmake, ["--build", build, "--parallel", "2"], request.workingDirectory) { outputFiles = [direct.outputFile], environmentOverrides = NativeBuildEnvironment.cleanCompiler, linkDependencyTrace = direct.commands[^1].linkDependencyTrace }
        ];
        generated.Add(new("cmake-project/CMakeLists.txt", text.ToString()));
        return direct with
        {
            providerId = id,
            commands = commands,
            generatedInputFiles = generated.ToImmutable(),
            externalInputFiles = direct.externalInputFiles.AddRange(ImmutableArray.Create(m_cmake, m_ninja)).Distinct(StringComparer.Ordinal).ToImmutableArray(),
            linkDependencyScans = direct.linkDependencyScans.Select(scan => scan with { finalCommandId = "build" }).ToImmutableArray()
        };
    }
    private static string Bracket(string value)
    {
        if (value.Contains('\0')) throw new ArgumentException("A native process argument cannot contain a NUL character.");
        string delimiter = string.Empty;
        while (value.Contains("]" + delimiter + "]", StringComparison.Ordinal)) delimiter += "=";
        return "[" + delimiter + "[" + value + "]" + delimiter + "]";
    }
    private static string RequireTool(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path)) throw new FileNotFoundException("A build-system executable must exist at an absolute path.", path);
        return path;
    }
    private static string Quote(string value)
    {
        if (value.Contains("$<", StringComparison.Ordinal) || value.Contains('\0') || value.Contains('\n') || value.Contains('\r'))
            throw new ArgumentException("A CMake build argument cannot contain generator expressions or control characters.");
        return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("$", "\\$", StringComparison.Ordinal).Replace(";", "\\;", StringComparison.Ordinal) + "\"";
    }
}
